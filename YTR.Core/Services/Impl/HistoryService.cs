using Microsoft.EntityFrameworkCore;
using YTR.Core.Data;
using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services.Impl;

public sealed class HistoryService : IHistoryService
{
    private readonly IDbContextFactory<YtrDbContext> _dbFactory;

    public HistoryService(IDbContextFactory<YtrDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<DownloadRecord>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Downloads
            .OrderByDescending(d => d.DownloadedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DownloadRecord>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Downloads
            .OrderByDescending(d => d.DownloadedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task RecordAsync(DownloadRecord record, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Downloads.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DownloadRecord record, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Downloads.Update(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await db.Downloads.FindAsync([id], ct);
        if (record is not null)
        {
            db.Downloads.Remove(record);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task CleanExpiredAsync(int retentionDays, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        await db.Downloads
            .Where(d => d.DownloadedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public async Task ClearAsync(StreamKind? filter = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (filter is null)
        {
            await db.Downloads.ExecuteDeleteAsync(ct);
        }
        else
        {
            await db.Downloads
                .Where(d => d.StreamKind == filter)
                .ExecuteDeleteAsync(ct);
        }
    }

    public async Task ClearWithFilesAsync(StreamKind? filter = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = filter is null
            ? db.Downloads.AsQueryable()
            : db.Downloads.Where(d => d.StreamKind == filter);

        var records = await query.ToListAsync(ct);

        foreach (var record in records)
        {
            try
            {
                if (record.InSubFolder && !string.IsNullOrEmpty(record.FilePath))
                {
                    var dir = Path.GetDirectoryName(record.FilePath);
                    if (dir is not null && Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                else if (!string.IsNullOrEmpty(record.FilePath) && File.Exists(record.FilePath))
                {
                    File.Delete(record.FilePath);
                }
            }
            catch
            {
                // Best effort file deletion — don't fail the whole operation
            }
        }

        if (filter is null)
            await db.Downloads.ExecuteDeleteAsync(ct);
        else
            await db.Downloads.Where(d => d.StreamKind == filter).ExecuteDeleteAsync(ct);
    }
}

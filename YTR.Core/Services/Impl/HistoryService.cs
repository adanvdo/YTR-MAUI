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

    public async Task<DownloadRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Downloads.FirstOrDefaultAsync(d => d.Id == id, ct);
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
                .Where(MatchesFilter(filter.Value))
                .ExecuteDeleteAsync(ct);
        }
    }

    public async Task ClearWithFilesAsync(StreamKind? filter = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = filter is null
            ? db.Downloads.AsQueryable()
            : db.Downloads.Where(MatchesFilter(filter.Value));

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
            await db.Downloads.Where(MatchesFilter(filter.Value)).ExecuteDeleteAsync(ct);
    }

    public async Task<int> CountAsync(StreamKind? filter = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return filter is null
            ? await db.Downloads.CountAsync(ct)
            : await db.Downloads.Where(MatchesFilter(filter.Value)).CountAsync(ct);
    }

    public async Task<int> CountMissingAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // File existence can't be evaluated in SQL, so materialize and check on disk.
        var records = await db.Downloads.ToListAsync(ct);
        return records.Count(IsFileMissing);
    }

    public async Task<int> ClearMissingAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var records = await db.Downloads.ToListAsync(ct);
        var missing = records.Where(IsFileMissing).ToList();

        if (missing.Count == 0)
            return 0;

        db.Downloads.RemoveRange(missing);
        await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    /// <summary>
    /// True when a record points to a file that no longer exists on disk.
    /// Records without a file path are treated as missing.
    /// </summary>
    private static bool IsFileMissing(DownloadRecord record) =>
        string.IsNullOrEmpty(record.FilePath) || !File.Exists(record.FilePath);

    /// <summary>
    /// Builds a predicate for a stream-kind filter. A "Video" filter matches any record
    /// that has a video component (both video-only and muxed audio+video downloads),
    /// since most video downloads are stored as <see cref="StreamKind.AudioAndVideo"/>.
    /// An "Audio" filter matches audio-only records.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<DownloadRecord, bool>> MatchesFilter(StreamKind filter) =>
        filter switch
        {
            StreamKind.Video => d => d.StreamKind == StreamKind.Video || d.StreamKind == StreamKind.AudioAndVideo,
            _ => d => d.StreamKind == filter,
        };
}

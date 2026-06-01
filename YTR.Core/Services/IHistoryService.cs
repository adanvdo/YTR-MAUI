using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Manages download history persistence and queries.
/// </summary>
public interface IHistoryService
{
    Task<IReadOnlyList<DownloadRecord>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DownloadRecord>> GetRecentAsync(int count, CancellationToken ct = default);
    Task RecordAsync(DownloadRecord record, CancellationToken ct = default);
    Task UpdateAsync(DownloadRecord record, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task CleanExpiredAsync(int retentionDays, CancellationToken ct = default);
    Task ClearAsync(StreamKind? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Clears history records and optionally deletes the associated files from disk.
    /// </summary>
    Task ClearWithFilesAsync(StreamKind? filter = null, CancellationToken ct = default);
}

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
    Task<DownloadRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task RecordAsync(DownloadRecord record, CancellationToken ct = default);
    Task UpdateAsync(DownloadRecord record, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task CleanExpiredAsync(int retentionDays, CancellationToken ct = default);
    Task ClearAsync(StreamKind? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Clears history records and optionally deletes the associated files from disk.
    /// </summary>
    Task ClearWithFilesAsync(StreamKind? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Counts the history records matching the given stream-kind filter (null = all).
    /// </summary>
    Task<int> CountAsync(StreamKind? filter = null, CancellationToken ct = default);

    /// <summary>
    /// Counts history records whose associated file no longer exists on disk.
    /// </summary>
    Task<int> CountMissingAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes history records whose associated file no longer exists on disk.
    /// Returns the number of records removed.
    /// </summary>
    Task<int> ClearMissingAsync(CancellationToken ct = default);
}

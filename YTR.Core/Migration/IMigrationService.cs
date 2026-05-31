namespace YTR.Core.Migration;

/// <summary>
/// Migrates data from the old YT-RED app (settings.json + history.json) to the new format.
/// </summary>
public interface IMigrationService
{
    /// <summary>
    /// Returns true if legacy data was detected and migration has not yet been completed.
    /// </summary>
    bool IsMigrationNeeded();

    /// <summary>
    /// Runs the migration. Safe to call multiple times — will no-op if already completed.
    /// </summary>
    Task<MigrationResult> MigrateAsync(CancellationToken ct = default);
}

public sealed record MigrationResult
{
    public bool Success { get; init; }
    public int SettingsMigrated { get; init; }
    public int HistoryRecordsMigrated { get; init; }
    public string? Error { get; init; }

    public static MigrationResult NotNeeded => new() { Success = true };
}

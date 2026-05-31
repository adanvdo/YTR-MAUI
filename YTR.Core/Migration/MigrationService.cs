using System.Text.Json;
using Microsoft.Extensions.Logging;
using YTR.Core.Configuration;
using YTR.Core.Data;
using YTR.Core.Enums;
using YTR.Core.Models;
using YTR.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace YTR.Core.Migration;

/// <summary>
/// Detects and migrates legacy YT-RED data (settings.json + history.json) on first launch.
/// </summary>
public sealed class MigrationService : IMigrationService
{
    private readonly IPlatformService _platform;
    private readonly ISettingsService _settings;
    private readonly IDbContextFactory<YtrDbContext> _dbFactory;
    private readonly ILogger<MigrationService> _logger;

    private const string MigrationMarkerFile = ".migration_complete";

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public MigrationService(
        IPlatformService platform,
        ISettingsService settings,
        IDbContextFactory<YtrDbContext> dbFactory,
        ILogger<MigrationService> logger)
    {
        _platform = platform;
        _settings = settings;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public bool IsMigrationNeeded()
    {
        var markerPath = Path.Combine(_platform.AppDataDirectory, MigrationMarkerFile);
        if (File.Exists(markerPath))
            return false;

        // Look for old settings.json in common locations
        var legacyPath = FindLegacyDirectory();
        return legacyPath is not null;
    }

    public async Task<MigrationResult> MigrateAsync(CancellationToken ct = default)
    {
        var markerPath = Path.Combine(_platform.AppDataDirectory, MigrationMarkerFile);
        if (File.Exists(markerPath))
            return MigrationResult.NotNeeded;

        var legacyDir = FindLegacyDirectory();
        if (legacyDir is null)
        {
            await MarkCompleteAsync(markerPath);
            return MigrationResult.NotNeeded;
        }

        _logger.LogInformation("Legacy YT-RED data found at {Path}. Starting migration.", legacyDir);

        int settingsMigrated = 0;
        int historyMigrated = 0;

        try
        {
            // Migrate settings
            var settingsPath = Path.Combine(legacyDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                settingsMigrated = await MigrateSettingsAsync(settingsPath, ct);
            }

            // Migrate history
            var historyPath = Path.Combine(legacyDir, "history.json");
            if (File.Exists(historyPath))
            {
                historyMigrated = await MigrateHistoryAsync(historyPath, ct);
            }

            await MarkCompleteAsync(markerPath);

            _logger.LogInformation(
                "Migration complete. Settings: {Settings}, History records: {History}",
                settingsMigrated, historyMigrated);

            return new MigrationResult
            {
                Success = true,
                SettingsMigrated = settingsMigrated,
                HistoryRecordsMigrated = historyMigrated
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration failed.");
            // Still mark complete to avoid retrying a broken migration on every launch
            await MarkCompleteAsync(markerPath);
            return new MigrationResult
            {
                Success = false,
                SettingsMigrated = settingsMigrated,
                HistoryRecordsMigrated = historyMigrated,
                Error = ex.Message
            };
        }
    }

    #region Settings Migration

    private async Task<int> MigrateSettingsAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        var legacy = JsonSerializer.Deserialize<LegacyAppSettings>(json, LegacyJsonOptions);

        if (legacy is null) return 0;

        int count = 0;

        if (legacy.General is not null)
        {
            var g = legacy.General;
            _settings.Download.VideoDownloadPath = g.VideoDownloadPath ?? _settings.Download.VideoDownloadPath;
            _settings.Download.AudioDownloadPath = g.AudioDownloadPath ?? _settings.Download.AudioDownloadPath;
            _settings.Download.UseTitleAsFileName = g.UseTitleAsFileName;
            _settings.Download.CreateFolderForPlaylists = g.CreateFolderForPlaylists;
            _settings.Download.AutoOpenDownloadLocation = g.AutomaticallyOpenDownloadLocation;

            _settings.Restrictions.EnforceRestrictions = g.EnforceRestrictions;
            _settings.Restrictions.MaxResolution = MapResolution(g.MaxResolutionBest);
            _settings.Restrictions.MaxFileSizeMb = g.MaxFilesizeBest;

            _settings.History.Enabled = g.EnableDownloadHistory;
            _settings.History.RetentionDays = g.HistoryAge;

            _settings.Updates.YtDlpLocalVersion = g.YtdlpLocalVersion ?? "";
            _settings.Updates.FfmpegLocalVersion = g.FfmpegLocalVersion ?? "";

            count++;
        }

        if (legacy.Advanced is not null)
        {
            var a = legacy.Advanced;
            _settings.Processing.AlwaysConvertToPreferred = a.AlwaysConvertToPreferredFormat;
            _settings.Processing.PreferredVideoFormat = MapVideoFormat(a.PreferredVideoFormat);
            _settings.Processing.PreferredAudioFormat = MapAudioFormat(a.PreferredAudioFormat);
            _settings.Processing.FetchMissingMetadata = a.GetMissingMetadata;
            _settings.Processing.VerboseOutput = a.VerboseOutput;

            _settings.Updates.Channel = MapReleaseChannel(a.Channel);

            count++;
        }

        if (legacy.Layout is not null)
        {
            var l = legacy.Layout;
            _settings.Appearance.FormatMode = l.FormatMode == 0 ? FormatMode.Preset : FormatMode.Custom;
            _settings.Appearance.SegmentMode = l.SegmentControlMode == 0 ? SegmentMode.Duration : SegmentMode.EndTime;

            count++;
        }

        await _settings.SaveAsync(ct);
        return count;
    }

    #endregion

    #region History Migration

    private async Task<int> MigrateHistoryAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        var legacyRecords = JsonSerializer.Deserialize<List<LegacyDownloadLog>>(json, LegacyJsonOptions);

        if (legacyRecords is null || legacyRecords.Count == 0)
            return 0;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Ensure database is created
        await db.Database.EnsureCreatedAsync(ct);

        int migrated = 0;
        foreach (var legacy in legacyRecords)
        {
            if (string.IsNullOrEmpty(legacy.Url) || string.IsNullOrEmpty(legacy.DownloadLocation))
                continue;

            var record = new DownloadRecord
            {
                Id = legacy.DownloadID ?? Guid.NewGuid(),
                Url = legacy.Url,
                Title = legacy.Title ?? "",
                Platform = MapPlatform(legacy.DownloadType),
                StreamKind = MapStreamKind(legacy.StreamType),
                DownloadedAt = legacy.Downloaded,
                FilePath = legacy.DownloadLocation,
                Format = legacy.Format,
                InSubFolder = legacy.InSubFolder,
                PlaylistTitle = legacy.PlaylistTitle,
                PlaylistUrl = legacy.PlaylistUrl,
                SegmentStart = legacy.Start,
                SegmentDuration = legacy.Duration,
                CropValues = legacy.Crops is not null ? string.Join(",", legacy.Crops) : null,
                VideoConversion = legacy.VideoConversionFormat.HasValue
                    ? MapVideoFormat(legacy.VideoConversionFormat.Value)
                    : null,
                AudioConversion = legacy.AudioConversionFormat.HasValue
                    ? MapAudioFormat(legacy.AudioConversionFormat.Value)
                    : null,
                MaxResolution = legacy.MaxResolution.HasValue
                    ? MapResolution(legacy.MaxResolution.Value)
                    : null,
                MaxFileSizeMb = legacy.MaxFileSize,
                SegmentMode = legacy.SegmentMode == 0 ? SegmentMode.Duration : SegmentMode.EndTime
            };

            db.Downloads.Add(record);
            migrated++;
        }

        await db.SaveChangesAsync(ct);
        return migrated;
    }

    #endregion

    #region Enum Mapping

    private static MediaPlatform MapPlatform(int oldDownloadType) => oldDownloadType switch
    {
        0 => MediaPlatform.YouTube,
        1 => MediaPlatform.Reddit,
        2 => MediaPlatform.Twitter,
        3 => MediaPlatform.Vimeo,
        4 => MediaPlatform.Instagram,
        5 => MediaPlatform.Twitch,
        6 => MediaPlatform.YouTube, // "Playlist" was a pseudo-type, always YouTube
        9 => MediaPlatform.TikTok,
        _ => MediaPlatform.Unknown
    };

    private static StreamKind MapStreamKind(int oldStreamType) => oldStreamType switch
    {
        0 => StreamKind.Video,
        1 => StreamKind.Audio,
        2 => StreamKind.AudioAndVideo,
        _ => StreamKind.Unknown
    };

    private static Resolution MapResolution(int oldResolution) => oldResolution switch
    {
        0 => Resolution.Sd,
        1 => Resolution.Hd720,
        2 => Resolution.Hd1080,
        3 => Resolution.Uhd2160,
        _ => Resolution.Any
    };

    private static VideoFormat MapVideoFormat(int oldFormat) => oldFormat switch
    {
        0 => VideoFormat.Mp4,
        1 => VideoFormat.Webm,
        2 => VideoFormat.Flv,
        3 => VideoFormat.Mkv,
        4 => VideoFormat.Ogg,
        5 => VideoFormat.Unspecified,
        6 => VideoFormat.Gif,
        _ => VideoFormat.Unspecified
    };

    private static AudioFormat MapAudioFormat(int oldFormat) => oldFormat switch
    {
        0 => AudioFormat.Mp3,
        1 => AudioFormat.M4a,
        2 => AudioFormat.Aac,
        3 => AudioFormat.Ogg,
        4 => AudioFormat.Wav,
        5 => AudioFormat.Flac,
        6 => AudioFormat.Opus,
        7 => AudioFormat.Vorbis,
        _ => AudioFormat.Unspecified
    };

    private static ReleaseChannel MapReleaseChannel(int oldChannel) => oldChannel switch
    {
        0 => ReleaseChannel.Stable,
        1 => ReleaseChannel.Beta,
        2 => ReleaseChannel.Alpha,
        _ => ReleaseChannel.Stable
    };

    #endregion

    #region Helpers

    /// <summary>
    /// Searches common locations for the old YT-RED installation directory.
    /// </summary>
    private static string? FindLegacyDirectory()
    {
        // The old app stored settings.json next to the exe.
        // Common install locations:
        var candidates = new List<string>();

        // Check Program Files
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (!string.IsNullOrEmpty(programFiles))
            candidates.Add(Path.Combine(programFiles, "YTR"));
        if (!string.IsNullOrEmpty(programFilesX86))
            candidates.Add(Path.Combine(programFilesX86, "YTR"));

        // Check user's Desktop and Downloads (portable installs)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            candidates.Add(Path.Combine(userProfile, "Desktop", "YTR"));
            candidates.Add(Path.Combine(userProfile, "Downloads", "YTR"));
        }

        // Check common dev paths
        candidates.Add(@"E:\Dev\YT-RED\YT-RED\bin\x86\Debug");
        candidates.Add(@"E:\Dev\YT-RED\YT-RED\bin\x64\Debug");
        candidates.Add(@"E:\Dev\YT-RED\YT-RED\bin\x86\Release");
        candidates.Add(@"E:\Dev\YT-RED\YT-RED\bin\x64\Release");

        // Check LocalAppData
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            candidates.Add(Path.Combine(localAppData, "YTR"));

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "settings.json")))
                return candidate;
        }

        return null;
    }

    private static async Task MarkCompleteAsync(string markerPath)
    {
        var dir = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(markerPath, $"Migrated at {DateTime.UtcNow:O}");
    }

    #endregion
}

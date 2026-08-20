using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using YTR.Core;
using YTR.Core.Data;
using YTR.Core.Migration;
using YTR.Core.Services;
using YTR.Maui.Logging;
using Microsoft.EntityFrameworkCore;

namespace YTR.Maui;

public static class MauiProgram
{
    /// <summary>
    /// The canonical app data directory for YTR on Windows: %LOCALAPPDATA%\YTR.
    /// Used for settings, database, logs, and temp files. Must match WindowsPlatformService.AppDataDirectory.
    /// </summary>
    internal static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTR");

    public static MauiApp CreateMauiApp()
    {
        // Ensure the app data directory exists before anything writes to it
        Directory.CreateDirectory(AppDataDir);

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
        builder.Logging.AddFilter("YTR.Core.Services.Impl.ProcessRunner", LogLevel.Debug);
        builder.Logging.AddFilter("YTR.Core.Services.Impl.YtDlpService", LogLevel.Debug);
        builder.Logging.AddFilter("YTR.Core.Services.Impl.FfmpegMediaProcessor", LogLevel.Debug);
#endif

        // File logging — always active, captures Warning+ to daily-rotated files.
        // Read saved log directory from settings (if configured); otherwise use default.
        var defaultLogDir = Path.Combine(AppDataDir, "Logs");
        var logDir = GetConfiguredLogDirectory() ?? defaultLogDir;
        builder.Logging.AddFile(logDir, minLevel: LogLevel.Warning, retainDays: 30);

        // MudBlazor
        builder.Services.AddMudServices();

        // UI state services
        builder.Services.AddSingleton<YTR.Web.Services.DownloadStateService>();

        // Platform service (must be registered before AddYtrCore since SettingsService depends on it)
#if WINDOWS
        builder.Services.AddSingleton<IPlatformService, Platforms.Windows.WindowsPlatformService>();
        builder.Services.AddSingleton<IHotkeyService, Platforms.Windows.WindowsHotkeyService>();
        builder.Services.AddSingleton<ITrayService, Platforms.Windows.WindowsTrayService>();
        builder.Services.AddSingleton<INotificationService, Platforms.Windows.WindowsNotificationService>();
        builder.Services.AddSingleton<IFolderPickerService, Platforms.Windows.WindowsFolderPickerService>();
        builder.Services.AddSingleton<IElevationService, Platforms.Windows.WindowsElevationService>();
        builder.Services.AddSingleton<Platforms.Windows.QuickDownloadHandler>();
        builder.Services.AddSingleton<Platforms.Windows.SingleInstanceGuard>();
#elif ANDROID
        builder.Services.AddSingleton<IPlatformService, Platforms.Android.AndroidPlatformService>();
#endif

        // Core services + database
        var dbPath = Path.Combine(AppDataDir, "ytr.db");
        builder.Services.AddYtrCore(dbPath);

        // Startup initializer
        builder.Services.AddTransient<AppStartup>();

        return builder.Build();
    }

    /// <summary>
    /// Reads the configured log directory from the persisted settings.json file.
    /// This runs before DI is built, so it does a raw JSON parse.
    /// Returns null if no custom path is configured.
    /// </summary>
    private static string? GetConfiguredLogDirectory()
    {
        try
        {
            var settingsPath = Path.Combine(AppDataDir, "settings.json");
            if (!File.Exists(settingsPath))
                return null;

            var json = File.ReadAllText(settingsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("logging", out var logging) &&
                logging.TryGetProperty("logDirectory", out var logDir))
            {
                var value = logDir.GetString();
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                    return value;
            }
        }
        catch
        {
            // If we can't read settings, fall back to default
        }

        return null;
    }
}

/// <summary>
/// Handles async startup tasks: DB creation, migration, settings load.
/// Called from App.xaml.cs on launch.
/// </summary>
public sealed class AppStartup
{
    private readonly IDbContextFactory<YtrDbContext> _dbFactory;
    private readonly IMigrationService _migration;
    private readonly ISettingsService _settings;
    private readonly IToolVersionService _toolVersionService;
    private readonly ILogger<AppStartup> _logger;

    public AppStartup(
        IDbContextFactory<YtrDbContext> dbFactory,
        IMigrationService migration,
        ISettingsService settings,
        IToolVersionService toolVersionService,
        ILogger<AppStartup> logger)
    {
        _dbFactory = dbFactory;
        _migration = migration;
        _settings = settings;
        _toolVersionService = toolVersionService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // Ensure database exists
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        // Migrate legacy data if needed
        if (_migration.IsMigrationNeeded())
        {
            _logger.LogInformation("Legacy data detected, running migration...");
            var result = await _migration.MigrateAsync();
            if (result.Success)
            {
                _logger.LogInformation(
                    "Migration complete: {Settings} settings sections, {History} history records.",
                    result.SettingsMigrated, result.HistoryRecordsMigrated);
            }
            else
            {
                _logger.LogWarning("Migration had errors: {Error}", result.Error);
            }
        }

        // Load settings
        await _settings.LoadAsync();

        // Detect actual tool versions from binaries (updates stored versions if stale)
        await _toolVersionService.DetectVersionsAsync();
    }
}

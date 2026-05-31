using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using YTR.Core;
using YTR.Core.Data;
using YTR.Core.Migration;
using YTR.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace YTR.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
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
#endif

        // MudBlazor
        builder.Services.AddMudServices();

        // UI state services
        builder.Services.AddScoped<YTR.Web.Services.DownloadStateService>();

        // Platform service (must be registered before AddYtrCore since SettingsService depends on it)
#if WINDOWS
        builder.Services.AddSingleton<IPlatformService, Platforms.Windows.WindowsPlatformService>();
        builder.Services.AddSingleton<IHotkeyService, Platforms.Windows.WindowsHotkeyService>();
        builder.Services.AddSingleton<ITrayService, Platforms.Windows.WindowsTrayService>();
        builder.Services.AddSingleton<INotificationService, Platforms.Windows.WindowsNotificationService>();
        builder.Services.AddSingleton<Platforms.Windows.QuickDownloadHandler>();
        builder.Services.AddSingleton<Platforms.Windows.SingleInstanceGuard>();
#elif ANDROID
        builder.Services.AddSingleton<IPlatformService, Platforms.Android.AndroidPlatformService>();
#endif

        // Core services + database
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ytr.db");
        builder.Services.AddYtrCore(dbPath);

        // Startup initializer
        builder.Services.AddTransient<AppStartup>();

        return builder.Build();
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
    private readonly ILogger<AppStartup> _logger;

    public AppStartup(
        IDbContextFactory<YtrDbContext> dbFactory,
        IMigrationService migration,
        ISettingsService settings,
        ILogger<AppStartup> logger)
    {
        _dbFactory = dbFactory;
        _migration = migration;
        _settings = settings;
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
    }
}

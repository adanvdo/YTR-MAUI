using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YTR.Core.Configuration;
using YTR.Core.Data;
using YTR.Core.Migration;
using YTR.Core.Services;
using YTR.Core.Services.Impl;

namespace YTR.Core;

/// <summary>
/// Extension methods to register all YTR.Core services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core services. Platform-specific services (IPlatformService) must be registered separately.
    /// </summary>
    public static IServiceCollection AddYtrCore(this IServiceCollection services, string dbPath)
    {
        // Database
        services.AddDbContextFactory<YtrDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Settings (singleton — loaded once, shared across app)
        services.AddSingleton<ISettingsService, SettingsService>();

        // Services
        services.AddSingleton<IUrlAnalyzer, UrlAnalyzer>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddScoped<IMediaProbeService, FfprobeMediaProbeService>();
        services.AddScoped<IHistoryService, HistoryService>();
        services.AddScoped<IYtDlpService, YtDlpService>();
        services.AddScoped<IMediaProcessor, FfmpegMediaProcessor>();
        services.AddScoped<IDownloadOrchestrator, DownloadOrchestrator>();
        services.AddScoped<IAppUpdateService, AppUpdateService>();
        services.AddScoped<IDependencyUpdateService, DependencyUpdateService>();

        // Options (bound from ISettingsService after load)
        services.AddOptions<DownloadOptions>()
            .Configure<ISettingsService>((opts, svc) =>
            {
                opts.VideoDownloadPath = svc.Download.VideoDownloadPath;
                opts.AudioDownloadPath = svc.Download.AudioDownloadPath;
                opts.UseTitleAsFileName = svc.Download.UseTitleAsFileName;
                opts.CreateFolderForPlaylists = svc.Download.CreateFolderForPlaylists;
                opts.AutoOpenDownloadLocation = svc.Download.AutoOpenDownloadLocation;
            });

        services.AddOptions<RestrictionOptions>()
            .Configure<ISettingsService>((opts, svc) =>
            {
                opts.EnforceRestrictions = svc.Restrictions.EnforceRestrictions;
                opts.MaxResolution = svc.Restrictions.MaxResolution;
                opts.MaxFileSizeMb = svc.Restrictions.MaxFileSizeMb;
            });

        services.AddOptions<ProcessingOptions>()
            .Configure<ISettingsService>((opts, svc) =>
            {
                opts.AlwaysConvertToPreferred = svc.Processing.AlwaysConvertToPreferred;
                opts.PreferredVideoFormat = svc.Processing.PreferredVideoFormat;
                opts.PreferredAudioFormat = svc.Processing.PreferredAudioFormat;
                opts.FetchMissingMetadata = svc.Processing.FetchMissingMetadata;
                opts.VerboseOutput = svc.Processing.VerboseOutput;
            });

        services.AddHttpClient();

        // Migration
        services.AddTransient<IMigrationService, MigrationService>();

        return services;
    }
}

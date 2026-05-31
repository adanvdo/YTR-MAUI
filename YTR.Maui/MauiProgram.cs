using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using YTR.Core.Data;
using YTR.Core.Services;
using YTR.Core.Services.Impl;

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

        // Database
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ytr.db");
        builder.Services.AddDbContextFactory<YtrDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Core services
        builder.Services.AddSingleton<IUrlAnalyzer, UrlAnalyzer>();
        builder.Services.AddScoped<IHistoryService, HistoryService>();
        builder.Services.AddHttpClient();

        // Platform services will be registered per-platform
#if WINDOWS
        // builder.Services.AddSingleton<IPlatformService, WindowsPlatformService>();
#elif ANDROID
        // builder.Services.AddSingleton<IPlatformService, AndroidPlatformService>();
#endif

        return builder.Build();
    }
}

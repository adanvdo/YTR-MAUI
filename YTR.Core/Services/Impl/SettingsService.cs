using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using YTR.Core.Configuration;
using YTR.Core.Enums;

namespace YTR.Core.Services.Impl;

/// <summary>
/// JSON file-backed settings service with atomic write (write-to-temp + rename).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public DownloadOptions Download { get; private set; } = new();
    public RestrictionOptions Restrictions { get; private set; } = new();
    public ProcessingOptions Processing { get; private set; } = new();
    public AppearanceOptions Appearance { get; private set; } = new();
    public UpdateOptions Updates { get; private set; } = new();
    public HistoryOptions History { get; private set; } = new();
    public WindowStateOptions WindowState { get; private set; } = new();

    public SettingsService(IPlatformService platform, ILogger<SettingsService> logger)
    {
        _settingsPath = Path.Combine(platform.AppDataDirectory, "settings.json");
        _logger = logger;

        // Set platform-appropriate defaults
        Download.VideoDownloadPath = platform.DefaultVideoPath;
        Download.AudioDownloadPath = platform.DefaultAudioPath;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.LogInformation("No settings file found, using defaults.");
                await SaveInternalAsync(ct);
                return;
            }

            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            var container = JsonSerializer.Deserialize<SettingsContainer>(json, JsonOptions);

            if (container is not null)
            {
                Download = container.Download ?? new();
                Restrictions = container.Restrictions ?? new();
                Processing = container.Processing ?? new();
                Appearance = container.Appearance ?? new();
                Updates = container.Updates ?? new();
                History = container.History ?? new();
                WindowState = container.WindowState ?? new();
            }

            _logger.LogInformation("Settings loaded from {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings, using defaults.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SaveInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(CancellationToken ct)
    {
        var container = new SettingsContainer
        {
            Download = Download,
            Restrictions = Restrictions,
            Processing = Processing,
            Appearance = Appearance,
            Updates = Updates,
            History = History,
            WindowState = WindowState
        };

        var json = JsonSerializer.Serialize(container, JsonOptions);

        // Atomic write: write to temp file, then rename
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tempPath = _settingsPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct);
        File.Move(tempPath, _settingsPath, overwrite: true);

        _logger.LogDebug("Settings saved to {Path}", _settingsPath);
    }

    /// <summary>
    /// Internal container for serialization.
    /// </summary>
    private sealed class SettingsContainer
    {
        public DownloadOptions? Download { get; set; }
        public RestrictionOptions? Restrictions { get; set; }
        public ProcessingOptions? Processing { get; set; }
        public AppearanceOptions? Appearance { get; set; }
        public UpdateOptions? Updates { get; set; }
        public HistoryOptions? History { get; set; }
        public WindowStateOptions? WindowState { get; set; }
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Download.VideoDownloadPath))
            return "Video download path is required.";
        if (string.IsNullOrWhiteSpace(Download.AudioDownloadPath))
            return "Audio download path is required.";
        if (Processing.PreferredVideoFormat == VideoFormat.Gif)
            return "Preferred video format cannot be GIF.";
        return null;
    }
}

using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Checks for and installs application updates.
/// </summary>
public interface IAppUpdateService
{
    Task<Result<AppRelease?>> CheckForUpdateAsync(ReleaseChannel channel, CancellationToken ct = default);
    Task<Result<string>> DownloadUpdateAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken ct = default);
    Task<Result> InstallUpdateAsync(string packagePath, CancellationToken ct = default);
}

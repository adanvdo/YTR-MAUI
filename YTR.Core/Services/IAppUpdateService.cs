using YTR.Core.Enums;
using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Checks for and installs application updates via GitHub Releases.
/// Nothing is automatic — the user must explicitly trigger check and install.
/// </summary>
public interface IAppUpdateService
{
    Task<Result<AppRelease?>> CheckForUpdateAsync(ReleaseChannel channel, CancellationToken ct = default);
    Task<Result<string>> DownloadUpdateAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken ct = default);
    Task InstallUpdateAsync(string installerPath);
}

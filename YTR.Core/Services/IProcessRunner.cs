using YTR.Core.Models;

namespace YTR.Core.Services;

/// <summary>
/// Cross-platform process execution abstraction.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default);

    /// <summary>
    /// Kills all currently running child processes spawned by this runner.
    /// </summary>
    void KillAll();
}

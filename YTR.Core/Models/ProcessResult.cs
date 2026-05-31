namespace YTR.Core.Models;

/// <summary>
/// Result of an external process execution.
/// </summary>
public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public bool WasCancelled { get; init; }
    public bool Success => ExitCode == 0 && !WasCancelled;
}

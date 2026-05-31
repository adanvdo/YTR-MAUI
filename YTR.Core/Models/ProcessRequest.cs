namespace YTR.Core.Models;

/// <summary>
/// Request to execute an external process.
/// </summary>
public sealed record ProcessRequest
{
    public required string Executable { get; init; }
    public required string Arguments { get; init; }
    public string? WorkingDirectory { get; init; }
    public IProgress<double>? Progress { get; init; }
    public Action<string>? OnOutputLine { get; init; }
}

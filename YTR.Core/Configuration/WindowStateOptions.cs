namespace YTR.Core.Configuration;

/// <summary>
/// Persisted window state (position, size, maximized).
/// </summary>
public sealed class WindowStateOptions
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Width { get; set; } = 1188;
    public int Height { get; set; } = 800;
    public bool Maximized { get; set; }
}

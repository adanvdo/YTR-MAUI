namespace YTR.Core.Models;

/// <summary>
/// A simple IProgress implementation that invokes the callback directly on the calling thread
/// without posting to a SynchronizationContext. Use in service layers where Progress&lt;T&gt;
/// would capture a null SynchronizationContext and fail to notify the UI.
/// </summary>
internal sealed class DirectProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public DirectProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value) => _handler(value);
}

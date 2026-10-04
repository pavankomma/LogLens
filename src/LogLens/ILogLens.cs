namespace LogLens;

/// <summary>The logger abstraction. Implementations must be thread-safe and must never throw from <see cref="Write"/>.</summary>
public interface ILogLensLogger
{
    /// <summary>Returns true if events at <paramref name="level"/> would be processed.</summary>
    bool IsEnabled(LogLevel level);

    /// <summary>Writes an event. Callers should check <see cref="IsEnabled"/> first to avoid allocations.</summary>
    void Write(LogLevel level, Exception? exception, string messageTemplate, params object?[] args);

    /// <summary>Pushes a property onto the ambient (async-local) scope until the returned handle is disposed.</summary>
    IDisposable BeginScope(string name, object? value);

    /// <summary>Returns a child logger that attaches <paramref name="name"/>=<paramref name="value"/> to every event.</summary>
    ILogLensLogger ForContext(string name, object? value);
}

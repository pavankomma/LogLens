namespace LogLens.Sinks;

/// <summary>Collects events in memory. Intended for tests.</summary>
public sealed class InMemorySink : ILogSink
{
    private readonly List<LogEvent> _events = [];
    private readonly object _sync = new();

    /// <summary>Snapshot of captured events in emission order.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_sync) return _events.ToArray();
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        lock (_sync) _events.Add(logEvent);
    }

    /// <summary>Removes all captured events.</summary>
    public void Clear()
    {
        lock (_sync) _events.Clear();
    }

    /// <inheritdoc />
    public void Flush() { }

    /// <inheritdoc />
    public void Dispose() { }
}

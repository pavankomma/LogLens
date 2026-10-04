namespace LogLens.Sinks;

/// <summary>Forwards only events at or above a minimum level to an inner sink (per-sink level restriction).</summary>
public sealed class LevelFilterSink : ILogSink
{
    private readonly ILogSink _inner;

    /// <summary>Creates a filtering wrapper.</summary>
    public LevelFilterSink(ILogSink inner, LogLevel minimumLevel)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        MinimumLevel = minimumLevel;
    }

    /// <summary>Events below this level are dropped.</summary>
    public LogLevel MinimumLevel { get; }

    /// <summary>The wrapped sink.</summary>
    public ILogSink Inner => _inner;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is not null && logEvent.Level >= MinimumLevel) _inner.Emit(logEvent);
    }

    /// <inheritdoc />
    public void Flush() => _inner.Flush();

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}

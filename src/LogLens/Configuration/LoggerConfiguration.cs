using LogLens.Sinks;

namespace LogLens.Configuration;

/// <summary>Fluent builder for an <see cref="LogLensLogger"/> pipeline.</summary>
public sealed class LoggerConfiguration
{
    private readonly List<ILogSink> _sinks = [];
    private readonly List<KeyValuePair<string, object?>> _enrichers = [];
    private LogLevel _minimumLevel = LogLevel.Information;
    private Func<DateTimeOffset>? _clock;
    private int? _asyncQueueLimit;
    private OverflowPolicy _overflow = OverflowPolicy.DropOldest;
    private TimeSpan? _shutdownTimeout;

    /// <summary>Creates a configuration.</summary>
    public LoggerConfiguration() => WriteTo = new SinkConfiguration(this);

    /// <summary>Sink registration entry point, e.g. <c>.WriteTo.Console()</c>.</summary>
    public SinkConfiguration WriteTo { get; }

    /// <summary>Sets the minimum level (default Information).</summary>
    public LoggerConfiguration MinimumLevel(LogLevel level)
    {
        _minimumLevel = level;
        return this;
    }

    /// <summary>Attaches a fixed property to every event.</summary>
    public LoggerConfiguration Enrich(string name, object? value)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Property name is required.", nameof(name));
        _enrichers.Add(new KeyValuePair<string, object?>(name, value));
        return this;
    }

    /// <summary>Overrides the time source (mainly for tests).</summary>
    public LoggerConfiguration Clock(Func<DateTimeOffset> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        return this;
    }

    /// <summary>Wraps all sinks in a background queue so callers never block on I/O.</summary>
    public LoggerConfiguration Async(int queueLimit = 10_000, OverflowPolicy overflow = OverflowPolicy.DropOldest, TimeSpan? shutdownTimeout = null)
    {
        if (queueLimit <= 0) throw new ArgumentOutOfRangeException(nameof(queueLimit));
        _asyncQueueLimit = queueLimit;
        _overflow = overflow;
        _shutdownTimeout = shutdownTimeout;
        return this;
    }

    /// <summary>Adds a custom sink.</summary>
    public LoggerConfiguration AddSink(ILogSink sink)
    {
        _sinks.Add(sink ?? throw new ArgumentNullException(nameof(sink)));
        return this;
    }

    /// <summary>Adds a custom sink that only receives events at or above <paramref name="restrictedToMinimumLevel"/>.</summary>
    public LoggerConfiguration AddSink(ILogSink sink, LogLevel restrictedToMinimumLevel)
        => AddSink(restrictedToMinimumLevel <= LogLevel.Trace ? sink : new LevelFilterSink(sink, restrictedToMinimumLevel));

    /// <summary>Builds the logger. The caller owns it and should dispose it at shutdown.</summary>
    public LogLensLogger CreateLogger()
    {
        ILogSink sink = _sinks.Count switch
        {
            0 => new AggregateSink([]),
            1 => _sinks[0],
            _ => new AggregateSink(_sinks),
        };

        if (_asyncQueueLimit is { } limit)
            sink = new AsyncSink(sink, limit, _overflow, _shutdownTimeout);

        var root = new LogLensLogger(sink, _minimumLevel, _clock);
        return _enrichers.Count == 0 ? root : root.WithContext(_enrichers);
    }
}

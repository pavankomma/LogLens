using LogLens.Sinks;
using LogLens.Templates;

namespace LogLens;

/// <summary>
/// The concrete logger. Build one with <see cref="Configuration.LoggerConfiguration"/>; dispose it (or call
/// <see cref="Log.CloseAndFlush"/>) at shutdown so async sinks drain.
/// </summary>
public sealed class LogLensLogger : ILogLensLogger, IDisposable
{
    private readonly ILogSink _sink;
    private readonly LogLevel _minimumLevel;
    private readonly KeyValuePair<string, object?>[] _context;
    private readonly string? _category;
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _ownsSink;
    private int _disposed;

    /// <summary>Creates a logger that writes to <paramref name="sink"/>.</summary>
    public LogLensLogger(ILogSink sink, LogLevel minimumLevel = LogLevel.Information, Func<DateTimeOffset>? clock = null)
        : this(sink, minimumLevel, clock ?? (() => DateTimeOffset.Now), [], null, ownsSink: true) { }

    private LogLensLogger(ILogSink sink, LogLevel minimumLevel, Func<DateTimeOffset> clock, KeyValuePair<string, object?>[] context, string? category, bool ownsSink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _minimumLevel = minimumLevel;
        _clock = clock;
        _context = context;
        _category = category;
        _ownsSink = ownsSink;
    }

    /// <summary>A logger that discards everything.</summary>
    public static LogLensLogger None { get; } = new(new NullSink(), LogLevel.None);

    /// <summary>Configured minimum level.</summary>
    public LogLevel MinimumLevel => _minimumLevel;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel level) => level >= _minimumLevel && level < LogLevel.None && _disposed == 0;

    /// <inheritdoc />
    public void Write(LogLevel level, Exception? exception, string messageTemplate, params object?[] args)
    {
        if (!IsEnabled(level)) return;
        try
        {
            messageTemplate ??= string.Empty;
            args ??= [];

            Dictionary<string, object?>? properties = null;
            if (_context.Length > 0 || !ScopeContext.IsEmpty)
            {
                properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var kv in _context) properties[kv.Key] = kv.Value;
                ScopeContext.CopyTo(properties);
            }

            var template = messageTemplate.Length == 0 ? null : MessageTemplate.Parse(messageTemplate);
            if (template is not null && args.Length > 0 && template.PlaceholderNames.Count > 0)
            {
                properties ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                template.BindProperties(args, properties);
            }

            var logEvent = new LogEvent(_clock(), level, messageTemplate, args, properties, exception, _category);
            _sink.Emit(logEvent);
        }
        catch (Exception ex)
        {
            SelfLog.Write("LogLens.Write failed", ex);
        }
    }

    /// <inheritdoc />
    public IDisposable BeginScope(string name, object? value) => ScopeContext.Push(name, value);

    /// <inheritdoc />
    public ILogLensLogger ForContext(string name, object? value)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Property name is required.", nameof(name));
        var context = new KeyValuePair<string, object?>[_context.Length + 1];
        _context.CopyTo(context, 0);
        context[_context.Length] = new KeyValuePair<string, object?>(name, value);
        return new LogLensLogger(_sink, _minimumLevel, _clock, context, _category, ownsSink: false);
    }

    /// <summary>Used by the configuration builder: returns an owning root logger with fixed context properties.</summary>
    internal LogLensLogger WithContext(IEnumerable<KeyValuePair<string, object?>> context)
        => new(_sink, _minimumLevel, _clock, context.ToArray(), _category, ownsSink: true);

    /// <summary>Returns a child logger whose events carry <paramref name="category"/>.</summary>
    public LogLensLogger ForCategory(string category)
        => new(_sink, _minimumLevel, _clock, _context, category, ownsSink: false);

    /// <summary>Returns a child logger whose events carry the type name as category.</summary>
    public LogLensLogger ForCategory<T>() => ForCategory(typeof(T).FullName ?? typeof(T).Name);

    /// <summary>Flushes the sink pipeline.</summary>
    public void Flush()
    {
        try { _sink.Flush(); }
        catch (Exception ex) { SelfLog.Write("LogLens.Flush failed", ex); }
    }

    /// <summary>Flushes and disposes the sink pipeline (only for the root logger that owns it).</summary>
    public void Dispose()
    {
        if (!_ownsSink) return;
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _sink.Dispose(); }
        catch (Exception ex) { SelfLog.Write("LogLens.Dispose failed", ex); }
    }

    private sealed class NullSink : ILogSink
    {
        public void Emit(LogEvent logEvent) { }
        public void Flush() { }
        public void Dispose() { }
    }
}

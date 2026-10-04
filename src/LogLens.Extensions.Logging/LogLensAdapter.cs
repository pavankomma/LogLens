using Microsoft.Extensions.Logging;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace LogLens.Extensions.Logging;

/// <summary>Adapts one MEL category to an <see cref="ILogLensLogger"/>.</summary>
public sealed class LogLensAdapter : ILogger
{
    private const string OriginalFormat = "{OriginalFormat}";
    private readonly ILogLensLogger _logger;
    private readonly string _category;

    /// <summary>Creates an adapter.</summary>
    public LogLensAdapter(ILogLensLogger logger, string category)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _category = category ?? string.Empty;
    }

    /// <summary>Maps an MEL level to an LogLens level.</summary>
    public static LogLevel ToLensLevel(MelLogLevel level) => level switch
    {
        MelLogLevel.Trace => LogLevel.Trace,
        MelLogLevel.Debug => LogLevel.Debug,
        MelLogLevel.Information => LogLevel.Information,
        MelLogLevel.Warning => LogLevel.Warning,
        MelLogLevel.Error => LogLevel.Error,
        MelLogLevel.Critical => LogLevel.Fatal,
        _ => LogLevel.None,
    };

    /// <inheritdoc />
    public bool IsEnabled(MelLogLevel logLevel) => _logger.IsEnabled(ToLensLevel(logLevel));

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            var handles = new List<IDisposable>();
            foreach (var kv in pairs)
            {
                if (kv.Key == OriginalFormat) continue;
                handles.Add(_logger.BeginScope(kv.Key, kv.Value));
            }

            return new CompositeScope(handles);
        }

        return _logger.BeginScope("Scope", state.ToString());
    }

    /// <inheritdoc />
    public void Log<TState>(MelLogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var level = ToLensLevel(logLevel);
        if (!_logger.IsEnabled(level)) return;

        var target = _logger.ForContext("Category", _category);
        if (eventId.Id != 0) target = target.ForContext("EventId", eventId.Id);

        if (state is IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            string? template = null;
            var args = new List<object?>(pairs.Count);
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].Key == OriginalFormat) template = pairs[i].Value as string;
                else args.Add(pairs[i].Value);
            }

            if (template is not null)
            {
                target.Write(level, exception, template, args.ToArray());
                return;
            }
        }

        target.Write(level, exception, "{Message}", formatter(state, exception));
    }

    private sealed class CompositeScope(List<IDisposable> handles) : IDisposable
    {
        public void Dispose()
        {
            for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
        }
    }
}

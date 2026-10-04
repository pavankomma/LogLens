using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LogLens.Extensions.Logging;

/// <summary>Routes <see cref="Microsoft.Extensions.Logging"/> output into an <see cref="ILogLensLogger"/>.</summary>
public sealed class LogLensProvider : ILoggerProvider
{
    private readonly ILogLensLogger _root;
    private readonly bool _dispose;
    private readonly ConcurrentDictionary<string, LogLensAdapter> _loggers = new(StringComparer.Ordinal);

    /// <summary>Creates a provider over <paramref name="logger"/>.</summary>
    /// <param name="logger">Target logger; if null, <see cref="Log.Logger"/> is resolved at write time.</param>
    /// <param name="dispose">Dispose the target logger when the provider is disposed.</param>
    public LogLensProvider(ILogLensLogger? logger = null, bool dispose = false)
    {
        _root = logger ?? new LogFacadeLogger();
        _dispose = dispose;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new LogLensAdapter(_root, name));

    /// <inheritdoc />
    public void Dispose()
    {
        _loggers.Clear();
        if (_dispose) (_root as IDisposable)?.Dispose();
    }

    /// <summary>Delegates to whatever <see cref="Log.Logger"/> is at call time.</summary>
    private sealed class LogFacadeLogger : ILogLensLogger
    {
        public bool IsEnabled(LogLevel level) => Log.Logger.IsEnabled(level);
        public void Write(LogLevel level, Exception? exception, string messageTemplate, params object?[] args) => Log.Logger.Write(level, exception, messageTemplate, args);
        public IDisposable BeginScope(string name, object? value) => Log.Logger.BeginScope(name, value);
        public ILogLensLogger ForContext(string name, object? value) => Log.Logger.ForContext(name, value);
    }
}

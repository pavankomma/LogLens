namespace LogLens;

/// <summary>
/// Static facade over a single process-wide logger. Assign <see cref="Logger"/> at startup and call
/// <see cref="CloseAndFlush"/> at shutdown.
/// </summary>
public static class Log
{
    private static ILogLensLogger _logger = LogLensLogger.None;

    static Log()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CloseAndFlush();
    }

    /// <summary>The active logger. Defaults to a silent logger.</summary>
    public static ILogLensLogger Logger
    {
        get => _logger;
        set => _logger = value ?? LogLensLogger.None;
    }

    /// <summary>True if <paramref name="level"/> is enabled on the active logger.</summary>
    public static bool IsEnabled(LogLevel level) => _logger.IsEnabled(level);

    /// <summary>Writes a Trace event.</summary>
    public static void Trace(string messageTemplate, params object?[] args) => _logger.Trace(messageTemplate, args);
    /// <summary>Writes a Trace event with an exception.</summary>
    public static void Trace(Exception? exception, string messageTemplate, params object?[] args) => _logger.Trace(exception, messageTemplate, args);

    /// <summary>Writes a Debug event.</summary>
    public static void Debug(string messageTemplate, params object?[] args) => _logger.Debug(messageTemplate, args);
    /// <summary>Writes a Debug event with an exception.</summary>
    public static void Debug(Exception? exception, string messageTemplate, params object?[] args) => _logger.Debug(exception, messageTemplate, args);

    /// <summary>Writes an Information event.</summary>
    public static void Info(string messageTemplate, params object?[] args) => _logger.Info(messageTemplate, args);
    /// <summary>Writes an Information event with an exception.</summary>
    public static void Info(Exception? exception, string messageTemplate, params object?[] args) => _logger.Info(exception, messageTemplate, args);

    /// <summary>Writes a Warning event.</summary>
    public static void Warn(string messageTemplate, params object?[] args) => _logger.Warn(messageTemplate, args);
    /// <summary>Writes a Warning event with an exception.</summary>
    public static void Warn(Exception? exception, string messageTemplate, params object?[] args) => _logger.Warn(exception, messageTemplate, args);

    /// <summary>Writes an Error event.</summary>
    public static void Error(string messageTemplate, params object?[] args) => _logger.Error(messageTemplate, args);
    /// <summary>Writes an Error event with an exception.</summary>
    public static void Error(Exception? exception, string messageTemplate, params object?[] args) => _logger.Error(exception, messageTemplate, args);

    /// <summary>Writes a Fatal event.</summary>
    public static void Fatal(string messageTemplate, params object?[] args) => _logger.Fatal(messageTemplate, args);
    /// <summary>Writes a Fatal event with an exception.</summary>
    public static void Fatal(Exception? exception, string messageTemplate, params object?[] args) => _logger.Fatal(exception, messageTemplate, args);

    /// <summary>Pushes an ambient scope property.</summary>
    public static IDisposable BeginScope(string name, object? value) => _logger.BeginScope(name, value);

    /// <summary>Returns a logger enriched with a fixed property.</summary>
    public static ILogLensLogger ForContext(string name, object? value) => _logger.ForContext(name, value);

    /// <summary>Flushes and disposes the active logger, then resets to the silent logger. Safe to call repeatedly.</summary>
    public static void CloseAndFlush()
    {
        var current = Interlocked.Exchange(ref _logger, LogLensLogger.None);
        if (ReferenceEquals(current, LogLensLogger.None)) return;
        (current as LogLensLogger)?.Flush();
        (current as IDisposable)?.Dispose();
    }
}

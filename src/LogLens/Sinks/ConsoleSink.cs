using LogLens.Configuration;
using LogLens.Formatting;

namespace LogLens.Sinks;

/// <summary>Writes to standard output (or standard error for Error/Fatal when enabled), optionally with ANSI colour.</summary>
public sealed class ConsoleSink : ILogSink
{
    private readonly ILogFormatter _formatter;
    private readonly ConsoleTheme _theme;
    private readonly bool _errorsToStdErr;
    private readonly TextWriter _out;
    private readonly TextWriter _err;
    private readonly object _sync = new();

    /// <summary>Creates a console sink.</summary>
    public ConsoleSink(ILogFormatter? formatter = null, ConsoleTheme theme = ConsoleTheme.None, bool errorsToStdErr = false)
        : this(formatter, theme, errorsToStdErr, Console.Out, Console.Error) { }

    internal ConsoleSink(ILogFormatter? formatter, ConsoleTheme theme, bool errorsToStdErr, TextWriter stdout, TextWriter stderr)
    {
        _formatter = formatter ?? new TextFormatter();
        _theme = theme;
        _errorsToStdErr = errorsToStdErr;
        _out = stdout;
        _err = stderr;
    }

    /// <summary>ANSI colour code for a level.</summary>
    internal static string Colour(LogLevel level) => level switch
    {
        LogLevel.Trace => "[90m",
        LogLevel.Debug => "[37m",
        LogLevel.Information => "[32m",
        LogLevel.Warning => "[33m",
        LogLevel.Error => "[31m",
        LogLevel.Fatal => "[97;41m",
        _ => string.Empty,
    };

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null) return;
        var writer = _errorsToStdErr && logEvent.Level >= LogLevel.Error ? _err : _out;
        lock (_sync)
        {
            if (_theme == ConsoleTheme.Ansi)
            {
                writer.Write(Colour(logEvent.Level));
                _formatter.Format(logEvent, writer);
                writer.Write("[0m");
            }
            else
            {
                _formatter.Format(logEvent, writer);
            }
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_sync)
        {
            _out.Flush();
            _err.Flush();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Flush();
}

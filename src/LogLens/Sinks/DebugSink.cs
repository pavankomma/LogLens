using LogLens.Formatting;

namespace LogLens.Sinks;

/// <summary>Writes to <see cref="System.Diagnostics.Debug"/> (visible in the IDE output window).</summary>
public sealed class DebugSink : ILogSink
{
    private readonly ILogFormatter _formatter;

    /// <summary>Creates a debug sink.</summary>
    public DebugSink(ILogFormatter? formatter = null) => _formatter = formatter ?? new TextFormatter();

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        using var sw = new StringWriter();
        _formatter.Format(logEvent, sw);
        System.Diagnostics.Debug.Write(sw.ToString());
    }

    /// <inheritdoc />
    public void Flush() => System.Diagnostics.Debug.Flush();

    /// <inheritdoc />
    public void Dispose() { }
}

using LogLens.Formatting;

namespace LogLens.Sinks;

/// <summary>Writes formatted events to any <see cref="TextWriter"/>. Thread-safe.</summary>
public class TextWriterSink : ILogSink
{
    private readonly TextWriter _writer;
    private readonly ILogFormatter _formatter;
    private readonly bool _ownsWriter;
    private readonly object _sync = new();

    /// <summary>Creates a sink over <paramref name="writer"/>.</summary>
    public TextWriterSink(TextWriter writer, ILogFormatter? formatter = null, bool ownsWriter = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _formatter = formatter ?? new TextFormatter();
        _ownsWriter = ownsWriter;
    }

    /// <inheritdoc />
    public virtual void Emit(LogEvent logEvent)
    {
        lock (_sync) _formatter.Format(logEvent, _writer);
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_sync) _writer.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Flush();
            if (_ownsWriter) _writer.Dispose();
        }
    }
}

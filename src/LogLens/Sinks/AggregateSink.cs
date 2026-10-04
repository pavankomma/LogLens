namespace LogLens.Sinks;

/// <summary>Fans an event out to several sinks, isolating each from the others' failures.</summary>
public sealed class AggregateSink : ILogSink
{
    private readonly ILogSink[] _sinks;

    /// <summary>Creates an aggregate over <paramref name="sinks"/>.</summary>
    public AggregateSink(IEnumerable<ILogSink> sinks)
    {
        if (sinks is null) throw new ArgumentNullException(nameof(sinks));
        _sinks = sinks.ToArray();
    }

    /// <summary>The wrapped sinks.</summary>
    public IReadOnlyList<ILogSink> Sinks => _sinks;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        foreach (var sink in _sinks)
        {
            try { sink.Emit(logEvent); }
            catch (Exception ex) { SelfLog.Write($"Sink {sink.GetType().Name} failed to emit", ex); }
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        foreach (var sink in _sinks)
        {
            try { sink.Flush(); }
            catch (Exception ex) { SelfLog.Write($"Sink {sink.GetType().Name} failed to flush", ex); }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var sink in _sinks)
        {
            try { sink.Dispose(); }
            catch (Exception ex) { SelfLog.Write($"Sink {sink.GetType().Name} failed to dispose", ex); }
        }
    }
}

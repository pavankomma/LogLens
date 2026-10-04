namespace LogLens.Sinks;

/// <summary>A destination for log events.</summary>
public interface ILogSink : IDisposable
{
    /// <summary>Writes one event. Implementations should not throw; the caller catches anyway.</summary>
    void Emit(LogEvent logEvent);

    /// <summary>Flushes buffered output to the underlying destination.</summary>
    void Flush();
}

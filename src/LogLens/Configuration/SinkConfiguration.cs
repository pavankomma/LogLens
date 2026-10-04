using LogLens.Formatting;
using LogLens.Sinks;

namespace LogLens.Configuration;

/// <summary>Registers built-in sinks on a <see cref="LoggerConfiguration"/>.</summary>
public sealed class SinkConfiguration
{
    private readonly LoggerConfiguration _owner;

    internal SinkConfiguration(LoggerConfiguration owner) => _owner = owner;

    /// <summary>Writes to the console.</summary>
    public LoggerConfiguration Console(ConsoleTheme theme = ConsoleTheme.None, string? outputTemplate = null, bool errorsToStdErr = false, LogLevel restrictedToMinimumLevel = LogLevel.Trace)
        => _owner.AddSink(new ConsoleSink(new TextFormatter(outputTemplate), theme, errorsToStdErr), restrictedToMinimumLevel);

    /// <summary>Writes to the console using a custom formatter.</summary>
    public LoggerConfiguration Console(ILogFormatter formatter, ConsoleTheme theme = ConsoleTheme.None, bool errorsToStdErr = false, LogLevel restrictedToMinimumLevel = LogLevel.Trace)
        => _owner.AddSink(new ConsoleSink(formatter, theme, errorsToStdErr), restrictedToMinimumLevel);

    /// <summary>Appends to a (rolling) file using the text formatter.</summary>
    public LoggerConfiguration File(
        string path,
        RollingInterval rollingInterval = RollingInterval.Infinite,
        long? fileSizeLimitBytes = null,
        int? retainedFileCount = null,
        string? outputTemplate = null,
        LogLevel restrictedToMinimumLevel = LogLevel.Trace)
        => _owner.AddSink(new FileSink(path, new TextFormatter(outputTemplate), rollingInterval, fileSizeLimitBytes, retainedFileCount), restrictedToMinimumLevel);

    /// <summary>Appends to a (rolling) file using a custom formatter, e.g. <see cref="JsonFormatter"/>.</summary>
    public LoggerConfiguration File(
        string path,
        ILogFormatter formatter,
        RollingInterval rollingInterval = RollingInterval.Infinite,
        long? fileSizeLimitBytes = null,
        int? retainedFileCount = null,
        LogLevel restrictedToMinimumLevel = LogLevel.Trace)
        => _owner.AddSink(new FileSink(path, formatter, rollingInterval, fileSizeLimitBytes, retainedFileCount), restrictedToMinimumLevel);

    /// <summary>Writes to <see cref="System.Diagnostics.Debug"/>.</summary>
    public LoggerConfiguration Debug(ILogFormatter? formatter = null) => _owner.AddSink(new DebugSink(formatter));

    /// <summary>Writes to any <see cref="TextWriter"/>.</summary>
    public LoggerConfiguration TextWriter(TextWriter writer, ILogFormatter? formatter = null, bool ownsWriter = false)
        => _owner.AddSink(new TextWriterSink(writer, formatter, ownsWriter));

    /// <summary>Collects events in memory (tests).</summary>
    public LoggerConfiguration InMemory(InMemorySink sink) => _owner.AddSink(sink);

    /// <summary>Adds any sink.</summary>
    public LoggerConfiguration Sink(ILogSink sink) => _owner.AddSink(sink);

    /// <summary>Adds any sink, restricted to events at or above <paramref name="restrictedToMinimumLevel"/>.</summary>
    public LoggerConfiguration Sink(ILogSink sink, LogLevel restrictedToMinimumLevel) => _owner.AddSink(sink, restrictedToMinimumLevel);
}

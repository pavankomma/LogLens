namespace LogLens.Formatting;

/// <summary>Converts a <see cref="LogEvent"/> into text.</summary>
public interface ILogFormatter
{
    /// <summary>Writes the formatted event (including any trailing newline) to <paramref name="output"/>.</summary>
    void Format(LogEvent logEvent, TextWriter output);
}

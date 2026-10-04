using System.Globalization;

namespace LogLens;

/// <summary>
/// Destination for the library's own internal failures (sink errors, formatter errors).
/// Disabled by default; set <see cref="Output"/> to a <see cref="TextWriter"/> to enable.
/// </summary>
public static class SelfLog
{
    private static readonly object Sync = new();

    /// <summary>Writer that receives internal error messages, or null to discard them.</summary>
    public static TextWriter? Output { get; set; }

    /// <summary>Writes a message to the self-log if enabled. Never throws.</summary>
    public static void Write(string message)
    {
        var output = Output;
        if (output is null) return;
        try
        {
            lock (Sync)
            {
                output.Write(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
                output.Write(' ');
                output.WriteLine(message);
                output.Flush();
            }
        }
        catch
        {
            // Self-log must never throw.
        }
    }

    /// <summary>Writes an exception with context to the self-log.</summary>
    public static void Write(string context, Exception exception) => Write(context + ": " + exception);
}

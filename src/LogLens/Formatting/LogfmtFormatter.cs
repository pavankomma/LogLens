using System.Globalization;
using System.Text;
using LogLens.Templates;

namespace LogLens.Formatting;

/// <summary>
/// Writes one logfmt line per event: <c>ts=… level=info msg="…" key=value …</c>.
/// Values containing spaces, quotes, <c>=</c> or control characters are quoted and backslash-escaped;
/// null property values render as an empty <c>key=</c>. Event properties follow the built-in fields and are
/// emitted under their own names, so a property named like a built-in field (e.g. <c>msg</c>) produces a
/// duplicate key — most logfmt consumers take the last occurrence.
/// </summary>
public sealed class LogfmtFormatter : ILogFormatter
{
    private readonly bool _includeTemplate;

    /// <summary>Creates a logfmt formatter.</summary>
    /// <param name="includeTemplate">Also emit the raw message template as a <c>template</c> field.</param>
    public LogfmtFormatter(bool includeTemplate = false) => _includeTemplate = includeTemplate;

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var sb = new StringBuilder(256);
        sb.Append("ts=").Append(logEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        sb.Append(" level=").Append(LevelName(logEvent.Level));
        AppendPair(sb, "msg", logEvent.RenderMessage());
        if (_includeTemplate) AppendPair(sb, "template", logEvent.MessageTemplate);
        if (logEvent.Category is not null) AppendPair(sb, "category", logEvent.Category);
        if (logEvent.Exception is not null) AppendPair(sb, "exception", logEvent.Exception.ToString());
        foreach (var kv in logEvent.Properties)
            AppendPair(sb, SafeKey(kv.Key), RenderValue(kv.Value));

        output.WriteLine(sb.ToString());
    }

    /// <summary>Conventional lowercase logfmt level name, e.g. <c>info</c>, <c>warn</c>.</summary>
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Fatal => "fatal",
        _ => "none",
    };

    private static string? RenderValue(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        _ => MessageTemplate.FormatValue(value, null, CultureInfo.InvariantCulture),
    };

    private static void AppendPair(StringBuilder sb, string key, string? value)
    {
        sb.Append(' ').Append(key).Append('=');
        if (string.IsNullOrEmpty(value)) return;

        if (!NeedsQuoting(value!))
        {
            sb.Append(value);
            return;
        }

        sb.Append('"');
        foreach (var c in value!)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }

    private static bool NeedsQuoting(string value)
    {
        foreach (var c in value)
            if (c <= ' ' || c is '"' or '=' or '\\') return true;
        return false;
    }

    private static string SafeKey(string key)
    {
        foreach (var c in key)
        {
            if (c <= ' ' || c is '"' or '=' or '\\')
            {
                var chars = key.ToCharArray();
                for (var i = 0; i < chars.Length; i++)
                    if (chars[i] <= ' ' || chars[i] is '"' or '=' or '\\') chars[i] = '_';
                return new string(chars);
            }
        }

        return key;
    }
}

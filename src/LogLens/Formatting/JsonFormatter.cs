using System.Globalization;
using System.Text;
using LogLens.Templates;

namespace LogLens.Formatting;

/// <summary>Writes one JSON object per line (NDJSON). No reflection; values are formatted invariantly.</summary>
public sealed class JsonFormatter : ILogFormatter
{
    private readonly bool _renderMessage;

    /// <summary>Creates a JSON formatter.</summary>
    /// <param name="renderMessage">Include a pre-rendered <c>message</c> field alongside the template.</param>
    public JsonFormatter(bool renderMessage = true) => _renderMessage = renderMessage;

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var sb = new StringBuilder(256);
        sb.Append("{\"timestamp\":\"").Append(logEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append('"');
        sb.Append(",\"level\":\"").Append(logEvent.Level.ToString()).Append('"');
        if (_renderMessage) AppendField(sb, "message", logEvent.RenderMessage());
        AppendField(sb, "template", logEvent.MessageTemplate);
        if (logEvent.Category is not null) AppendField(sb, "category", logEvent.Category);
        if (logEvent.Exception is not null) AppendField(sb, "exception", logEvent.Exception.ToString());

        if (logEvent.Properties.Count > 0)
        {
            sb.Append(",\"properties\":{");
            var first = true;
            foreach (var kv in logEvent.Properties)
            {
                if (!first) sb.Append(',');
                first = false;
                AppendString(sb, kv.Key);
                sb.Append(':');
                AppendValue(sb, kv.Value);
            }

            sb.Append('}');
        }

        sb.Append('}');
        output.WriteLine(sb.ToString());
    }

    private static void AppendField(StringBuilder sb, string name, string value)
    {
        sb.Append(",\"").Append(name).Append("\":");
        AppendString(sb, value);
    }

    private static void AppendValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case DateTimeOffset dto:
                AppendString(sb, dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                AppendString(sb, dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                AppendString(sb, MessageTemplate.FormatValue(value, null, CultureInfo.InvariantCulture));
                break;
        }
    }

    internal static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }
}

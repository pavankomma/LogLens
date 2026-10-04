using System.Globalization;
using LogLens.Templates;

namespace LogLens.Formatting;

/// <summary>
/// Human-readable single-line formatter driven by an output template. Supported holes:
/// <c>{Timestamp[:format]}</c>, <c>{Level[:u3|u|l]}</c>, <c>{Message}</c>, <c>{Category}</c>, <c>{Exception}</c>,
/// <c>{NewLine}</c>, <c>{Properties}</c>, and any event property by name. A newline is appended after the template;
/// <c>{Exception}</c> renders as a newline followed by the exception when one is present.
/// </summary>
public sealed class TextFormatter : ILogFormatter
{
    /// <summary>The default output template.</summary>
    public const string DefaultTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message}{Exception}";

    private static readonly string[] LevelNames3 = ["TRC", "DBG", "INF", "WRN", "ERR", "FTL", "OFF"];
    private readonly MessageTemplate _template;
    private readonly IFormatProvider _provider;

    /// <summary>Creates a formatter with the given output template.</summary>
    public TextFormatter(string? outputTemplate = null, IFormatProvider? formatProvider = null)
    {
        _template = MessageTemplate.Parse(outputTemplate ?? DefaultTemplate);
        _provider = formatProvider ?? CultureInfo.InvariantCulture;
    }

    /// <summary>Short three-letter name for a level, e.g. <c>INF</c>.</summary>
    public static string ShortLevelName(LogLevel level)
    {
        var i = (int)level;
        return i >= 0 && i < LevelNames3.Length ? LevelNames3[i] : level.ToString();
    }

    /// <inheritdoc />
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var names = _template.PlaceholderNames;
        var args = new object?[names.Count];
        for (var i = 0; i < names.Count; i++) args[i] = Resolve(names[i], logEvent);
        output.Write(_template.Render(args, _provider));
        output.WriteLine();
    }

    private object? Resolve(string name, LogEvent e) => name switch
    {
        "Timestamp" => e.Timestamp,
        "Level" => new LevelValue(e.Level),
        "Message" => e.RenderMessage(),
        "Category" => e.Category ?? string.Empty,
        "Exception" => e.Exception is null ? string.Empty : Environment.NewLine + e.Exception,
        "NewLine" => Environment.NewLine,
        "Properties" => RenderProperties(e),
        _ => e.Properties.TryGetValue(name, out var v) ? v : string.Empty,
    };

    private string RenderProperties(LogEvent e)
    {
        if (e.Properties.Count == 0) return string.Empty;
        var parts = new List<string>(e.Properties.Count);
        foreach (var kv in e.Properties)
            parts.Add(kv.Key + "=" + MessageTemplate.FormatValue(kv.Value, null, _provider));
        return "{" + string.Join(", ", parts) + "}";
    }

    /// <summary>Wraps a level so <c>{Level:u3}</c>, <c>{Level:u}</c> and <c>{Level:l}</c> formats work.</summary>
    private readonly struct LevelValue(LogLevel level) : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider) => format switch
        {
            "u3" => ShortLevelName(level),
            "u" => level.ToString().ToUpperInvariant(),
            "l" => level.ToString().ToLowerInvariant(),
            _ => level.ToString(),
        };

        public override string ToString() => level.ToString();
    }
}

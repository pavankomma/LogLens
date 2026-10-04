using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace LogLens.Templates;

/// <summary>
/// A parsed message template such as <c>"Order {Id} took {Elapsed:0.0,8} ms"</c>.
/// Templates are parsed once and cached by their raw text.
/// </summary>
public sealed class MessageTemplate
{
    private const int CacheLimit = 1000;
    private static readonly ConcurrentDictionary<string, MessageTemplate> Cache = new(StringComparer.Ordinal);

    private readonly Token[] _tokens;

    private MessageTemplate(string text, Token[] tokens, string[] placeholderNames)
    {
        Text = text;
        _tokens = tokens;
        PlaceholderNames = placeholderNames;
    }

    /// <summary>The raw template text.</summary>
    public string Text { get; }

    /// <summary>Distinct placeholder names in order of first appearance.</summary>
    public IReadOnlyList<string> PlaceholderNames { get; }

    /// <summary>Parses (or fetches from cache) a template.</summary>
    public static MessageTemplate Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (Cache.TryGetValue(text, out var cached)) return cached;
        var parsed = ParseCore(text);
        if (Cache.Count < CacheLimit) Cache.TryAdd(text, parsed);
        return parsed;
    }

    /// <summary>Renders the template with positional arguments.</summary>
    public string Render(IReadOnlyList<object?> args, IFormatProvider? provider = null)
    {
        if (_tokens.Length == 1 && _tokens[0].IsText) return _tokens[0].Value;
        var sb = new StringBuilder(Text.Length + 32);
        foreach (var token in _tokens)
        {
            if (token.IsText)
            {
                sb.Append(token.Value);
                continue;
            }

            if (token.Index >= args.Count)
            {
                sb.Append(token.Raw); // unbound placeholder renders literally
                continue;
            }

            var rendered = FormatValue(args[token.Index], token.Format, provider);
            if (token.Alignment != 0)
            {
                var pad = Math.Abs(token.Alignment) - rendered.Length;
                if (pad > 0)
                {
                    if (token.Alignment > 0) sb.Append(' ', pad).Append(rendered);
                    else sb.Append(rendered).Append(' ', pad);
                    continue;
                }
            }

            sb.Append(rendered);
        }

        return sb.ToString();
    }

    /// <summary>Binds positional arguments to placeholder names, writing into <paramref name="target"/>.</summary>
    public void BindProperties(IReadOnlyList<object?> args, IDictionary<string, object?> target)
    {
        var names = PlaceholderNames;
        var count = Math.Min(names.Count, args.Count);
        for (var i = 0; i < count; i++) target[names[i]] = args[i];
    }

    internal static string FormatValue(object? value, string? format, IFormatProvider? provider)
    {
        provider ??= CultureInfo.InvariantCulture;
        return value switch
        {
            null => "null",
            string s => s,
            IFormattable f => f.ToString(format, provider),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static MessageTemplate ParseCore(string text)
    {
        var tokens = new List<Token>();
        var names = new List<string>();
        var literal = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                var close = text.IndexOf('}', i + 1);
                if (close > i + 1 && TryParseHole(text, i + 1, close, out var name, out var format, out var align))
                {
                    if (literal.Length > 0)
                    {
                        tokens.Add(Token.TextToken(literal.ToString()));
                        literal.Clear();
                    }

                    var index = names.IndexOf(name);
                    if (index < 0)
                    {
                        names.Add(name);
                        index = names.Count - 1;
                    }

                    tokens.Add(Token.Hole(text.Substring(i, close - i + 1), index, format, align));
                    i = close + 1;
                    continue;
                }

                literal.Append('{');
                i++;
                continue;
            }

            if (c == '}' && i + 1 < text.Length && text[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0 || tokens.Count == 0) tokens.Add(Token.TextToken(literal.ToString()));
        return new MessageTemplate(text, tokens.ToArray(), names.ToArray());
    }

    private static bool TryParseHole(string text, int start, int end, out string name, out string? format, out int alignment)
    {
        name = string.Empty;
        format = null;
        alignment = 0;

        var body = text.Substring(start, end - start);
        var colon = body.IndexOf(':');
        var head = colon < 0 ? body : body.Substring(0, colon);
        if (colon >= 0) format = body.Substring(colon + 1);

        var comma = head.IndexOf(',');
        if (comma >= 0)
        {
            if (!int.TryParse(head.Substring(comma + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out alignment))
                return false;
            head = head.Substring(0, comma);
        }

        if (head.Length > 0 && (head[0] == '@' || head[0] == '$')) head = head.Substring(1);
        if (head.Length == 0) return false;
        foreach (var ch in head)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
        }

        name = head;
        return true;
    }

    private readonly struct Token
    {
        private Token(bool isText, string value, string raw, int index, string? format, int alignment)
        {
            IsText = isText;
            Value = value;
            Raw = raw;
            Index = index;
            Format = format;
            Alignment = alignment;
        }

        public bool IsText { get; }
        public string Value { get; }
        public string Raw { get; }
        public int Index { get; }
        public string? Format { get; }
        public int Alignment { get; }

        public static Token TextToken(string value) => new(true, value, value, -1, null, 0);
        public static Token Hole(string raw, int index, string? format, int alignment) => new(false, string.Empty, raw, index, format, alignment);
    }
}

using LogLens.Templates;

namespace LogLens;

/// <summary>An immutable log event produced by a logger and consumed by sinks.</summary>
public sealed class LogEvent
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyProperties = new Dictionary<string, object?>();
    private static readonly object?[] EmptyArgs = [];
    private string? _renderedMessage;

    /// <summary>Creates a log event.</summary>
    public LogEvent(
        DateTimeOffset timestamp,
        LogLevel level,
        string messageTemplate,
        object?[]? args = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        Exception? exception = null,
        string? category = null)
    {
        Timestamp = timestamp;
        Level = level;
        MessageTemplate = messageTemplate ?? throw new ArgumentNullException(nameof(messageTemplate));
        Args = args ?? EmptyArgs;
        Properties = properties ?? EmptyProperties;
        Exception = exception;
        Category = category;
    }

    /// <summary>When the event was created.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Severity.</summary>
    public LogLevel Level { get; }

    /// <summary>The raw message template, e.g. <c>"User {UserId} logged in"</c>.</summary>
    public string MessageTemplate { get; }

    /// <summary>Positional arguments bound to the template placeholders.</summary>
    public IReadOnlyList<object?> Args { get; }

    /// <summary>
    /// Structured properties: named template arguments plus scope and context enrichment.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>Attached exception, if any.</summary>
    public Exception? Exception { get; }

    /// <summary>Logical source of the event (e.g. a type name); may be null.</summary>
    public string? Category { get; }

    /// <summary>Renders the message template with its arguments. Cached after the first call.</summary>
    public string RenderMessage() => _renderedMessage ??= MessageTemplate.Length == 0
        ? string.Empty
        : Templates.MessageTemplate.Parse(MessageTemplate).Render(Args);
}

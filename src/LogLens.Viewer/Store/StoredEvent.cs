namespace LogLens.Viewer.Store;

/// <summary>An ingested event with a store-assigned sequence number.</summary>
public sealed class StoredEvent
{
    /// <summary>Monotonic sequence assigned at ingest (0 until stored).</summary>
    public long Seq { get; set; }

    /// <summary>Event time.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Severity.</summary>
    public LogLevel Level { get; init; }

    /// <summary>Rendered message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Original template, if provided.</summary>
    public string? Template { get; init; }

    /// <summary>Category, if provided.</summary>
    public string? Category { get; init; }

    /// <summary>Exception text, if provided.</summary>
    public string? Exception { get; init; }

    /// <summary>Structured properties (values already rendered to strings or JSON primitives).</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>Ingest source label (file path or "http").</summary>
    public string? Source { get; init; }
}

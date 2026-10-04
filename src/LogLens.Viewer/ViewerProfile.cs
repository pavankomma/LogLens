using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogLens.Viewer;

/// <summary>
/// Domain profile: teaches the viewer what your properties mean. Loaded from <c>--profile file.json</c>.
/// <code>
/// {
///   "name": "Shop",
///   "entities": { "OrderId": { "label": "Order", "link": "https://admin.example.com/orders/{value}" } },
///   "quickViews": [ { "name": "Failed payments", "query": "category:Shop.Payments level:error" } ],
///   "redact": [ "CardNumber", "Email" ],
///   "keys": { "identity": ["Customer"], "duration": ["Elapsed"], "route": ["Route"], "correlation": ["OrderId"] }
/// }
/// </code>
/// </summary>
public sealed class ViewerProfile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Display name of the profile.</summary>
    public string? Name { get; set; }

    /// <summary>Property name → entity metadata (label, link template with <c>{value}</c>).</summary>
    public Dictionary<string, Entity> Entities { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Named one-click filters shown in the UI.</summary>
    public List<QuickView> QuickViews { get; init; } = [];

    /// <summary>Property names whose values are masked in every API response.</summary>
    public List<string> Redact { get; init; } = [];

    /// <summary>Overrides for the UI's key detection.</summary>
    public KeyOverrides Keys { get; init; } = new();

    /// <summary>An entity definition.</summary>
    public sealed class Entity
    {
        /// <summary>Human label ("Order").</summary>
        public string? Label { get; set; }
        /// <summary>Link template; <c>{value}</c> is replaced with the property value.</summary>
        public string? Link { get; set; }
    }

    /// <summary>A saved one-click filter.</summary>
    public sealed class QuickView
    {
        /// <summary>Shown on the button.</summary>
        public string Name { get; set; } = "";
        /// <summary>DSL query applied on click.</summary>
        public string Query { get; set; } = "";
        /// <summary>Optional relative range (e.g. <c>-1h</c>).</summary>
        public string? From { get; set; }
    }

    /// <summary>Detection-list overrides; empty lists keep the built-in defaults.</summary>
    public sealed class KeyOverrides
    {
        /// <summary>Who: e.g. Customer, TenantId.</summary>
        [JsonPropertyName("identity")] public List<string> Identity { get; init; } = [];
        /// <summary>How long: e.g. Elapsed.</summary>
        [JsonPropertyName("duration")] public List<string> Duration { get; init; } = [];
        /// <summary>What endpoint: e.g. Route.</summary>
        [JsonPropertyName("route")] public List<string> Route { get; init; } = [];
        /// <summary>Chain key: e.g. OrderId, DocumentId.</summary>
        [JsonPropertyName("correlation")] public List<string> Correlation { get; init; } = [];
    }

    /// <summary>
    /// Loads a profile from a JSON file; returns an empty profile when <paramref name="path"/> is null, missing,
    /// or malformed (the problem is written to stderr — a bad profile must not stop the viewer).
    /// </summary>
    public static ViewerProfile Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return new ViewerProfile();
        try
        {
            var full = Path.GetFullPath(path!);
            return JsonSerializer.Deserialize<ViewerProfile>(File.ReadAllText(full), Json) ?? new ViewerProfile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"Profile '{path}' could not be loaded and is ignored: {ex.Message}");
            return new ViewerProfile();
        }
    }

    /// <summary>True when this property must be masked.</summary>
    public bool IsRedacted(string key) => Redact.Any(r => r.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Masks redacted properties; returns the original dictionary when nothing matches.</summary>
    public IReadOnlyDictionary<string, string> Mask(IReadOnlyDictionary<string, string> properties)
    {
        if (Redact.Count == 0) return properties;
        Dictionary<string, string>? copy = null;
        foreach (var kv in properties)
        {
            if (!IsRedacted(kv.Key)) continue;
            copy ??= new Dictionary<string, string>(properties, StringComparer.Ordinal);
            copy[kv.Key] = "•••";
        }

        return copy ?? properties;
    }
}

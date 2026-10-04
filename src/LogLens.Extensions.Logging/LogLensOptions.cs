using LogLens.Configuration;

namespace LogLens.Extensions.Logging;

/// <summary>
/// Options bound from configuration (conventionally the <c>Logging:LogLens</c> section):
/// <code>
/// "LogLens": {
///   "MinimumLevel": "Debug",
///   "Viewer": "http://localhost:5341", "ApiKey": "…", "ViewerMinimumLevel": "Information",
///   "Console": true, "ConsoleMinimumLevel": "Information",
///   "File": { "Path": "logs/app-.json", "Rolling": "Day", "Retain": 7, "SizeLimitBytes": null, "Json": true, "MinimumLevel": "Debug" },
///   "Async": true,
///   "Enrich": { "App": "MyService", "Env": "prod" }
/// }
/// </code>
/// </summary>
public sealed class LogLensOptions
{
    /// <summary>Overall minimum level (default Information).</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>Base URL of LogLens.Viewer (or any compatible endpoint). Null disables the HTTP sink.</summary>
    public string? Viewer { get; set; }

    /// <summary>API key sent as <c>X-Api-Key</c> to the viewer.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Minimum level for the viewer sink.</summary>
    public LogLevel ViewerMinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>Write to the console.</summary>
    public bool Console { get; set; }

    /// <summary>Minimum level for the console sink.</summary>
    public LogLevel ConsoleMinimumLevel { get; set; } = LogLevel.Trace;

    /// <summary>ANSI colours on the console.</summary>
    public bool ConsoleColors { get; set; } = true;

    /// <summary>Optional rolling file sink.</summary>
    public FileOptions? File { get; set; }

    /// <summary>Wrap all sinks in a background queue (default true).</summary>
    public bool Async { get; set; } = true;

    /// <summary>Fixed properties attached to every event.</summary>
    public Dictionary<string, string> Enrich { get; } = new(StringComparer.Ordinal);

    /// <summary>File sink options.</summary>
    public sealed class FileOptions
    {
        /// <summary>Path such as <c>logs/app-.json</c>; the rolling period is inserted before the extension.</summary>
        public string Path { get; set; } = "logs/app-.json";

        /// <summary>Rolling interval (default Day).</summary>
        public RollingInterval Rolling { get; set; } = RollingInterval.Day;

        /// <summary>Number of files to keep.</summary>
        public int? Retain { get; set; } = 7;

        /// <summary>Roll when a file exceeds this many bytes.</summary>
        public long? SizeLimitBytes { get; set; }

        /// <summary>Write NDJSON (true, default) or text.</summary>
        public bool Json { get; set; } = true;

        /// <summary>Minimum level for the file sink.</summary>
        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;
    }

    /// <summary>Builds a logger from these options.</summary>
    public global::LogLens.LogLensLogger CreateLogger()
    {
        var cfg = new LoggerConfiguration().MinimumLevel(MinimumLevel);
        foreach (var kv in Enrich) cfg.Enrich(kv.Key, kv.Value);
        if (Console) cfg.WriteTo.Console(ConsoleColors ? ConsoleTheme.Ansi : ConsoleTheme.None, restrictedToMinimumLevel: ConsoleMinimumLevel);
        if (File is { } f)
        {
            if (f.Json) cfg.WriteTo.File(f.Path, new Formatting.JsonFormatter(), f.Rolling, f.SizeLimitBytes, f.Retain, f.MinimumLevel);
            else cfg.WriteTo.File(f.Path, f.Rolling, f.SizeLimitBytes, f.Retain, restrictedToMinimumLevel: f.MinimumLevel);
        }

        if (!string.IsNullOrWhiteSpace(Viewer)) Sinks.Http.HttpSinkConfigurationExtensions.Http(cfg.WriteTo, Viewer!, ApiKey, restrictedToMinimumLevel: ViewerMinimumLevel);
        if (Async) cfg.Async();
        return cfg.CreateLogger();
    }
}

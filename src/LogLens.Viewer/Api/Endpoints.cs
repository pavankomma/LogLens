using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LogLens.Viewer.Ingest;
using LogLens.Viewer.Query;
using LogLens.Viewer.Store;

namespace LogLens.Viewer.Api;

/// <summary>HTTP API surface.</summary>
public static partial class Endpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Maps all API routes.</summary>
    public static IEndpointRouteBuilder MapViewerApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/ingest", async (HttpContext ctx, LogStore store, ViewerOptions options) =>
        {
            if (!Authorized(ctx, options)) return Results.Unauthorized();
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false);
            var events = EventParser.ParseMany(body, "http");
            var accepted = store.Add(events);
            return Results.Json(new { accepted }, Json);
        }).DisableAntiforgery()
          .WithTags("Ingest").WithSummary("Ingest NDJSON events")
          .WithDescription("Body: NDJSON (one JsonFormatter-shaped object per line) or a JSON array. Header X-Api-Key when configured. Returns {accepted:n}.");

        // Seq-compatible ingestion so stock Serilog.Sinks.Seq / seqcli / any CLEF producer can target the viewer.
        // https://datalust.co/docs/posting-raw-events — POST /ingest/clef (current) and /api/events/raw (legacy).
        async Task<IResult> IngestClef(HttpContext ctx, LogStore store, ViewerOptions options)
        {
            var key = ctx.Request.Headers["X-Seq-ApiKey"].FirstOrDefault() ?? ctx.Request.Query["apiKey"].FirstOrDefault();
            if (!string.IsNullOrEmpty(options.ApiKey) && key != options.ApiKey) return Results.Unauthorized();
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync(ctx.RequestAborted).ConfigureAwait(false);
            store.Add(EventParser.ParseMany(body, "serilog"));
            return Results.Text("""{"MinimumLevelAccepted":null}""", "application/json", statusCode: StatusCodes.Status201Created);
        }

        // OTLP/HTTP logs (binary protobuf) so OpenTelemetry exporters can target the viewer directly.
        app.MapPost("/v1/logs", async (HttpContext ctx, LogStore store, ViewerOptions options) =>
        {
            if (!Authorized(ctx, options)) return Results.Unauthorized();
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms, ctx.RequestAborted).ConfigureAwait(false);
            store.Add(OtlpLogsParser.Parse(ms.ToArray()));
            return Results.Bytes([], "application/x-protobuf");   // empty ExportLogsServiceResponse
        }).DisableAntiforgery()
          .WithTags("Ingest").WithSummary("Ingest OTLP/HTTP logs")
          .WithDescription("OTLP logs as binary protobuf (ExportLogsServiceRequest), so OpenTelemetry exporters can target the viewer directly.");

        app.MapPost("/ingest/clef", IngestClef).DisableAntiforgery()
           .WithTags("Ingest").WithSummary("Ingest CLEF events (Seq-compatible)")
           .WithDescription("Seq-compatible ingestion for Serilog.Sinks.Seq / seqcli / any CLEF producer. API key via X-Seq-ApiKey header or ?apiKey=.");
        app.MapPost("/api/events/raw", IngestClef).DisableAntiforgery()
           .WithTags("Ingest").WithSummary("Ingest CLEF events (legacy Seq route)");
        app.MapGet("/api", () => Results.Json(new { Product = "LogLens.Viewer", Version = "1.0.0", SeqCompatible = true }, Json))
           .WithTags("System").WithSummary("Product banner (Seq compatibility probe)");

        api.MapGet("/profile", (ViewerProfile profile) => Results.Json(new
        {
            name = profile.Name,
            entities = profile.Entities,
            quickViews = profile.QuickViews,
            redact = profile.Redact,
            keys = new { identity = profile.Keys.Identity, duration = profile.Keys.Duration, route = profile.Keys.Route, correlation = profile.Keys.Correlation },
        }, Json)).WithTags("System").WithSummary("Active deployment profile")
           .WithDescription("Display names, quick views, redaction list and key hints the UI adapts to.");

        api.MapGet("/events", (LogStore store, ViewerProfile profile, string? q, string? from, string? to, int? limit, long? before) =>
        {
            var now = DateTimeOffset.UtcNow;
            var predicate = QueryParser.Parse(q);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = store.Query(predicate, ParseTime(from, now), ParseTime(to, now), Math.Clamp(limit ?? 100, 1, 1000), before);
            var elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return Results.Json(new
            {
                events = result.Events.Select(e => ToDto(e, profile)),
                total = result.Total,
                facets = new { levels = result.Levels, categories = result.Categories, properties = MaskFacets(result.Properties, profile) },
                histogram = result.Histogram.Select(b => new { t = b.T, count = b.Count, warnings = b.Warnings, errors = b.Errors }),
                range = new { from = result.RangeStart, to = result.RangeEnd },
                elapsedMs = Math.Round(elapsedMs, 1),
            }, Json);
        }).WithTags("Events").WithSummary("Query events")
          .WithDescription("q = query DSL; from/to ISO-8601 or relative (-15m, -2h, -7d); limit ≤ 1000 (default 100); before = sequence cursor for paging. Returns events (newest first), total, facets, histogram and server-side query time.");

        // Dashboards: group by a key (level | category | source | any property) with error rates, duration percentiles, trends.
        api.MapGet("/aggregate", (LogStore store, ViewerProfile profile, string by, string? duration, string? q, string? from, string? to, int? limit, int? buckets, int? top) =>
        {
            if (string.IsNullOrWhiteSpace(by)) return Results.BadRequest(new { error = "'by' is required (level, category, source, or a property name)." });
            if (profile.IsRedacted(by)) return Results.BadRequest(new { error = $"'{by}' is redacted by the active profile." });
            var now = DateTimeOffset.UtcNow;
            var r = store.Aggregate(QueryParser.Parse(q), ParseTime(from, now), ParseTime(to, now), by.Trim(), string.IsNullOrWhiteSpace(duration) ? null : duration!.Trim(),
                Math.Clamp(limit ?? 50, 1, 500), Math.Clamp(buckets ?? 8, 1, 120), Math.Clamp(top ?? 3, 0, 10));
            return Results.Json(new
            {
                by,
                duration,
                total = r.Total,
                distinctKeys = r.DistinctKeys,
                unattributed = r.Unattributed,
                range = new { from = r.RangeStart, to = r.RangeEnd },
                groups = r.Groups.Select(g => new
                {
                    key = g.Key,
                    count = g.Count,
                    errors = g.Errors,
                    warnings = g.Warnings,
                    first = g.First,
                    last = g.Last,
                    p50 = g.P50,
                    p95 = g.P95,
                    max = g.Max,
                    status5xx = g.Status5xx,
                    top = g.Top,
                    trend = g.Trend,
                    trendP95 = g.TrendP95,
                    slowestSeq = g.SlowestSeq,
                    lastErrorSeq = g.LastErrorSeq,
                }),
            }, Json);
        }).WithTags("Dashboards").WithSummary("Aggregate events by a key")
          .WithDescription("Groups by level, category, source or any property: per group count, errors, warnings, first/last, p50/p95/max of the duration property, 5xx count, top templates, per-bucket trends, slowest/last-error seq.");

        api.MapGet("/events/{seq:long}", (LogStore store, ViewerProfile profile, long seq)
            => store.Get(seq) is { } e ? Results.Json(ToDto(e, profile), Json) : Results.NotFound())
           .WithTags("Events").WithSummary("Get one event by sequence number");

        api.MapDelete("/events", (HttpContext ctx, LogStore store, ViewerOptions options) =>
        {
            if (!Authorized(ctx, options)) return Results.Unauthorized();
            store.Clear();
            return Results.NoContent();
        }).WithTags("Events").WithSummary("Clear the store")
          .WithDescription("Dev convenience; requires X-Api-Key when an API key is configured.");

        api.MapGet("/stats", (LogStore store, IEnumerable<IHostedService> services) =>
        {
            var range = store.Range();
            var files = services.OfType<FileTailSource>().SelectMany(s => s.TrackedFiles).ToArray();
            return Results.Json(new
            {
                count = store.Count,
                capacity = store.Capacity,
                protectedCount = store.ProtectedCount,
                protectedCapacity = store.ProtectedCapacity,
                ingested = store.IngestedTotal,
                oldest = range?.Oldest,
                newest = range?.Newest,
                sources = new[] { "http" }.Concat(files),
            }, Json);
        }).WithTags("System").WithSummary("Store statistics")
          .WithDescription("Retained/ingested counts, ring and protected-buffer capacity, retained time range and active sources.");

        api.MapGet("/stream", async (HttpContext ctx, LogStore store, ViewerProfile profile, string? q) =>
        {
            var predicate = QueryParser.Parse(q);
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            var reader = store.Subscribe(ctx.RequestAborted); // subscribe before announcing, so nothing is missed
            await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted).ConfigureAwait(false);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);

            try
            {
                while (await reader.WaitToReadAsync(ctx.RequestAborted).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var e))
                    {
                        if (!predicate(e)) continue;
                        await ctx.Response.WriteAsync("data: " + JsonSerializer.Serialize(ToDto(e, profile), Json) + "\n\n", ctx.RequestAborted).ConfigureAwait(false);
                    }

                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* client went away */ }
        }).WithTags("Events").WithSummary("Live event stream (SSE)")
          .WithDescription("text/event-stream of events matching q as they arrive. Not usable from Swagger UI — connect with EventSource.");

        return app;
    }

    internal static bool Authorized(HttpContext ctx, ViewerOptions options)
        => string.IsNullOrEmpty(options.ApiKey) || ctx.Request.Headers["X-Api-Key"] == options.ApiKey;

    /// <summary>Parses ISO-8601 or relative (<c>-15m</c>, <c>-2h</c>, <c>-7d</c>, <c>now</c>) times.</summary>
    public static DateTimeOffset? ParseTime(string? s, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s!.Trim();
        if (s.Equals("now", StringComparison.OrdinalIgnoreCase)) return now;
        var m = RelativeRegex().Match(s);
        if (m.Success)
        {
            var n = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var span = m.Groups[2].Value switch
            {
                "s" => TimeSpan.FromSeconds(n),
                "m" => TimeSpan.FromMinutes(n),
                "h" => TimeSpan.FromHours(n),
                "d" => TimeSpan.FromDays(n),
                _ => TimeSpan.FromDays(n * 7),
            };
            return now - span;
        }

        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }

    private static object ToDto(StoredEvent e, ViewerProfile? profile = null) => new
    {
        seq = e.Seq,
        timestamp = e.Timestamp,
        level = e.Level.ToString(),
        message = e.Message,
        template = e.Template,
        category = e.Category,
        exception = e.Exception,
        properties = profile is null ? e.Properties : profile.Mask(e.Properties),
        source = e.Source,
    };

    private static IReadOnlyDictionary<string, Dictionary<string, int>> MaskFacets(IReadOnlyDictionary<string, Dictionary<string, int>> properties, ViewerProfile profile)
        => profile.Redact.Count == 0 ? properties : properties.Where(p => !profile.IsRedacted(p.Key)).ToDictionary(p => p.Key, p => p.Value);

    [GeneratedRegex(@"^-(\d+(?:\.\d+)?)([smhdw])$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeRegex();
}

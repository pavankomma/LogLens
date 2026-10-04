using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LogLens.Viewer.Api;
using LogLens.Viewer.Store;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogLens.Viewer.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Viewer:Capacity", "1000");
            b.ConfigureServices(s => s.AddSingleton(new ViewerOptions { Capacity = 1000 }));
        });
    }

    public void Dispose() => _factory.Dispose();

    private static StringContent Ndjson(params string[] lines) => new(string.Join('\n', lines), Encoding.UTF8, "application/x-ndjson");

    [Fact]
    public async Task Ingest_query_get_stats_and_delete()
    {
        var client = _factory.CreateClient();
        var post = await client.PostAsync("/api/ingest", Ndjson(
            "{\"timestamp\":\"2026-08-30T10:00:00Z\",\"level\":\"Information\",\"message\":\"hello\",\"properties\":{\"A\":\"1\"}}",
            "{\"timestamp\":\"2026-08-30T10:00:01Z\",\"level\":\"Error\",\"message\":\"boom\",\"category\":\"X\",\"exception\":\"ex\"}",
            "garbage"));
        post.EnsureSuccessStatusCode();
        Assert.Equal(2, (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetInt32());

        var all = await client.GetFromJsonAsync<JsonElement>("/api/events");
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal("boom", all.GetProperty("events")[0].GetProperty("message").GetString());
        Assert.Equal(1, all.GetProperty("facets").GetProperty("levels").GetProperty("Error").GetInt32());
        Assert.Equal(60, all.GetProperty("histogram").GetArrayLength());
        Assert.True(all.GetProperty("elapsedMs").GetDouble() >= 0);

        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/events?q=level:error&limit=5");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var seq = filtered.GetProperty("events")[0].GetProperty("seq").GetInt64();

        var one = await client.GetFromJsonAsync<JsonElement>($"/api/events/{seq}");
        Assert.Equal("X", one.GetProperty("category").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/events/999999")).StatusCode);

        var ranged = await client.GetFromJsonAsync<JsonElement>("/api/events?from=2026-08-30T10:00:00.5Z&to=now");
        Assert.Equal(1, ranged.GetProperty("total").GetInt32());

        var stats = await client.GetFromJsonAsync<JsonElement>("/api/stats");
        Assert.Equal(2, stats.GetProperty("count").GetInt32());
        Assert.Equal(1000, stats.GetProperty("capacity").GetInt32());
        Assert.Equal("http", stats.GetProperty("sources")[0].GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/events")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/stats")).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Api_key_guards_ingest_and_delete_but_not_reads()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(new ViewerOptions { ApiKey = "secret" })));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/ingest", Ndjson("{\"message\":\"m\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/events")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/events")).StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/ingest", Ndjson("{\"message\":\"m\"}"))).StatusCode);
    }

    [Fact]
    public async Task Stream_delivers_matching_events_as_sse()
    {
        var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stream?q=level:error");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        Assert.Equal(": connected", await reader.ReadLineAsync(cts.Token));

        var store = _factory.Services.GetRequiredService<LogStore>();
        store.Add([new StoredEvent { Timestamp = DateTimeOffset.UtcNow, Level = LogLevel.Information, Message = "skip" }]);
        store.Add([new StoredEvent { Timestamp = DateTimeOffset.UtcNow, Level = LogLevel.Error, Message = "take" }]);

        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null && !line.StartsWith("data:", StringComparison.Ordinal)) { }
        Assert.NotNull(line);
        var evt = JsonSerializer.Deserialize<JsonElement>(line!.Substring(5));
        Assert.Equal("take", evt.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Seq_compatible_clef_ingest_accepts_serilog_shape()
    {
        var client = _factory.CreateClient();
        var clef = """
            {"@t":"2026-08-30T10:00:00Z","@mt":"Hello {Name}","@l":"Warning","@i":"a1b2c3d4","@tr":"abc123","@sp":"def456","Name":"world","SourceContext":"My.App"}
            {"@t":"2026-08-30T10:00:01Z","@m":"rendered","@x":"System.Exception: boom"}
            """;
        var content = new StringContent(clef, Encoding.UTF8, "application/vnd.serilog.clef");
        var resp = await client.PostAsync("/ingest/clef", content);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Contains("MinimumLevelAccepted", await resp.Content.ReadAsStringAsync());

        var legacy = await client.PostAsync("/api/events/raw?clef", new StringContent("""{"@t":"2026-08-30T10:00:02Z","@mt":"legacy"}""", Encoding.UTF8, "application/vnd.serilog.clef"));
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);

        var all = await client.GetFromJsonAsync<JsonElement>("/api/events?q=Name=world");
        var e = all.GetProperty("events")[0];
        Assert.Equal("Warning", e.GetProperty("level").GetString());
        Assert.Equal("Hello {Name}", e.GetProperty("template").GetString());
        Assert.Equal("Hello world", e.GetProperty("message").GetString());
        Assert.Equal("My.App", e.GetProperty("category").GetString());
        Assert.Equal("abc123", e.GetProperty("properties").GetProperty("TraceId").GetString());
        Assert.Equal("def456", e.GetProperty("properties").GetProperty("SpanId").GetString());
        Assert.Equal("a1b2c3d4", e.GetProperty("properties").GetProperty("EventId").GetString());
        Assert.Equal("serilog", e.GetProperty("source").GetString());
        Assert.Equal(3, (await client.GetFromJsonAsync<JsonElement>("/api/events")).GetProperty("total").GetInt32());

        var info = await client.GetFromJsonAsync<JsonElement>("/api");
        Assert.True(info.GetProperty("seqCompatible").GetBoolean());
    }

    [Fact]
    public async Task Seq_ingest_honours_api_key_via_header_or_query()
    {
        using var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(new ViewerOptions { ApiKey = "k1" })));
        var client = factory.CreateClient();
        StringContent Body() => new("""{"@mt":"x"}""", Encoding.UTF8, "application/vnd.serilog.clef");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/ingest/clef", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/ingest/clef?apiKey=k1", Body())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Seq-ApiKey", "k1");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/ingest/clef", Body())).StatusCode);
    }

    [Fact]
    public async Task Serves_ui()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("LogLens Viewer", html);
        // The Vite build (ClientApp/) emits stable un-hashed filenames into wwwroot; see vite.config.ts.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/assets/index.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/assets/index.css")).StatusCode);
        Assert.Contains("assets/index.js", html);
    }

    [Fact]
    public async Task Serves_openapi_and_swagger_ui()
    {
        var client = _factory.CreateClient();
        var doc = await client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/events", doc);
        Assert.Contains("/api/ingest", doc);
        Assert.Contains("/ingest/clef", doc);
        var swagger = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Theory]
    [InlineData("-15m", -15 * 60)]
    [InlineData("-2h", -2 * 3600)]
    [InlineData("-1.5d", -36 * 3600)]
    [InlineData("-1w", -7 * 86400)]
    [InlineData("now", 0)]
    public static void ParseTime_relative(string input, int expectedOffsetSeconds)
    {
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(now.AddSeconds(expectedOffsetSeconds), Endpoints.ParseTime(input, now));
    }

    [Fact]
    public static void ParseTime_absolute_and_invalid()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), Endpoints.ParseTime("2026-01-02T03:04:05Z", now));
        Assert.Null(Endpoints.ParseTime("yesterday-ish", now));
        Assert.Null(Endpoints.ParseTime("", now));
    }

    [Fact]
    public static void Options_parse_args_and_config()
    {
        var o = ViewerOptions.Parse(["--capacity", "5", "--tail", "a", "--tail", "b", "--api-key", "k", "--demo", "--junk"]);
        Assert.True(o.Demo);
        Assert.Equal(5, o.Capacity);
        Assert.Equal(new[] { "a", "b" }, o.Tail);
        Assert.Equal("k", o.ApiKey);
        Assert.Equal(200_000, ViewerOptions.Parse(["--capacity", "nope"]).Capacity);
    }
}

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LogLens.Viewer.Store;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogLens.Viewer.Tests;

public sealed class ViewerProfileTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "LogLens.Viewer.Tests", Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try { File.Delete(_file); } catch (IOException) { }
    }

    [Fact]
    public void Load_parses_json_with_comments_and_tolerates_missing_or_broken_files()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, """
        {
          // comment
          "name": "Shop",
          "entities": { "OrderId": { "label": "Order", "link": "https://x/{value}" } },
          "quickViews": [ { "name": "Errors", "query": "level:error" } ],
          "redact": [ "CardNumber" ],
          "keys": { "correlation": [ "OrderId" ] },
        }
        """);
        var p = ViewerProfile.Load(_file);
        Assert.Equal("Shop", p.Name);
        Assert.Equal("Order", p.Entities["OrderId"].Label);
        Assert.Single(p.QuickViews);
        Assert.Equal(["OrderId"], p.Keys.Correlation);
        Assert.True(p.IsRedacted("cardnumber"));

        Assert.Null(ViewerProfile.Load(null).Name);
        Assert.Null(ViewerProfile.Load("does-not-exist.json").Name);
        File.WriteAllText(_file, "not json");
        Assert.Null(ViewerProfile.Load(_file).Name);
    }

    [Fact]
    public void Mask_replaces_only_redacted_values_and_is_allocation_free_when_unused()
    {
        var props = new Dictionary<string, string> { ["A"] = "1", ["Secret"] = "s3cr3t" };
        var none = new ViewerProfile();
        Assert.Same(props, none.Mask(props));

        var p = new ViewerProfile { Redact = { "secret" } };
        var masked = p.Mask(props);
        Assert.Equal("•••", masked["Secret"]);
        Assert.Equal("1", masked["A"]);
        Assert.Equal("s3cr3t", props["Secret"]);   // original untouched
    }

    [Fact]
    public async Task Api_serves_profile_and_masks_redacted_properties_everywhere()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton(new ViewerOptions());
            s.AddSingleton(new ViewerProfile { Name = "T", Redact = { "CardNumber" }, QuickViews = { new ViewerProfile.QuickView { Name = "E", Query = "level:error" } } });
        }));
        var client = factory.CreateClient();

        var prof = await client.GetFromJsonAsync<JsonElement>("/api/profile");
        Assert.Equal("T", prof.GetProperty("name").GetString());
        Assert.Equal("E", prof.GetProperty("quickViews")[0].GetProperty("name").GetString());

        var body = """{"message":"paid","properties":{"CardNumber":"4111111111111111","OrderId":"7"}}""";
        (await client.PostAsync("/api/ingest", new StringContent(body, Encoding.UTF8, "application/x-ndjson"))).EnsureSuccessStatusCode();

        var events = await client.GetFromJsonAsync<JsonElement>("/api/events");
        var props = events.GetProperty("events")[0].GetProperty("properties");
        Assert.Equal("•••", props.GetProperty("CardNumber").GetString());
        Assert.Equal("7", props.GetProperty("OrderId").GetString());
        Assert.False(events.GetProperty("facets").GetProperty("properties").TryGetProperty("CardNumber", out _));

        var agg = await client.GetAsync("/api/aggregate?by=CardNumber");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, agg.StatusCode);

        var one = await client.GetFromJsonAsync<JsonElement>("/api/events/" + events.GetProperty("events")[0].GetProperty("seq").GetInt64());
        Assert.Equal("•••", one.GetProperty("properties").GetProperty("CardNumber").GetString());
    }
}

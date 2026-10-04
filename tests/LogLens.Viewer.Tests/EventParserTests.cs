using LogLens.Formatting;
using LogLens.Viewer.Ingest;
using Xunit;

namespace LogLens.Viewer.Tests;

public class EventParserTests
{
    [Fact]
    public static void Parses_JsonFormatter_output_round_trip()
    {
        var ts = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
        var props = new Dictionary<string, object?> { ["OrderId"] = 1002, ["User"] = "ann", ["Ok"] = true, ["Nil"] = null };
        var e = new LogEvent(ts, LogLevel.Warning, "Order {OrderId} late", [1002], props, new InvalidOperationException("x"), "Orders");
        var sw = new StringWriter();
        new JsonFormatter().Format(e, sw);

        var parsed = EventParser.ParseMany(sw.ToString(), "test");
        var s = Assert.Single(parsed);
        Assert.Equal(ts, s.Timestamp);
        Assert.Equal(LogLevel.Warning, s.Level);
        Assert.Equal("Order 1002 late", s.Message);
        Assert.Equal("Order {OrderId} late", s.Template);
        Assert.Equal("Orders", s.Category);
        Assert.Contains("InvalidOperationException: x", s.Exception);
        Assert.Equal("1002", s.Properties["OrderId"]);
        Assert.Equal("ann", s.Properties["User"]);
        Assert.Equal("true", s.Properties["Ok"]);
        Assert.Equal("null", s.Properties["Nil"]);
        Assert.Equal("test", s.Source);
    }

    [Fact]
    public static void Accepts_json_array_and_skips_malformed_lines()
    {
        var body = "[{\"message\":\"a\"},{\"message\":\"b\",\"level\":\"err\"}]";
        var arr = EventParser.ParseMany(body, null);
        Assert.Equal(2, arr.Count);
        Assert.Equal(LogLevel.Error, arr[1].Level);

        var lines = "{\"message\":\"one\"}\nnot json\n\n{\"message\":\"two\"},\n{broken";
        var parsed = EventParser.ParseMany(lines, null);
        Assert.Equal(new[] { "one", "two" }, parsed.Select(p => p.Message));
        Assert.Empty(EventParser.ParseMany("   ", null));
    }

    [Fact]
    public static void Accepts_serilog_compact_shape_and_flat_properties()
    {
        var body = "{\"@t\":\"2026-01-01T00:00:00Z\",\"@l\":\"Warning\",\"@mt\":\"Hi {Name}\",\"Name\":\"bob\",\"SourceContext\":\"App.X\",\"@x\":\"boom\"}";
        var s = Assert.Single(EventParser.ParseMany(body, null));
        Assert.Equal(LogLevel.Warning, s.Level);
        Assert.Equal("Hi bob", s.Message); // no rendered message → template rendered from properties
        Assert.Equal("bob", s.Properties["Name"]);
        Assert.Equal("App.X", s.Category);
        Assert.Equal("boom", s.Exception);
        Assert.False(s.Properties.ContainsKey("@t"));
    }

    [Fact]
    public static void Missing_timestamp_uses_fallback_and_empty_object_is_rejected()
    {
        var fallback = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var s = Assert.Single(EventParser.ParseMany("{\"message\":\"m\"}", null, fallback));
        Assert.Equal(fallback, s.Timestamp);
        Assert.Empty(EventParser.ParseMany("{}", null));
        Assert.Empty(EventParser.ParseMany("[1,2]", null));
    }

    [Fact]
    public static void RenderTemplate_substitutes_known_holes_and_keeps_unknown()
    {
        var props = new Dictionary<string, string> { ["A"] = "1", ["B"] = "two" };
        Assert.Equal("1 two {C} {x} 1", EventParser.RenderTemplate("{A} {@B} {C} {{x}} {A:0.0}", props));
        Assert.Equal("plain", EventParser.RenderTemplate("plain", props));
    }

    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("VRB", LogLevel.Trace)]
    [InlineData("dbg", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("critical", LogLevel.Fatal)]
    [InlineData("nonsense", LogLevel.Information)]
    [InlineData(null, LogLevel.Information)]
    public static void ParseLevel_aliases(string? input, LogLevel expected) => Assert.Equal(expected, EventParser.ParseLevel(input));
}

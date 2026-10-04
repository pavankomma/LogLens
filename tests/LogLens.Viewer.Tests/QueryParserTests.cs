using LogLens.Viewer.Query;
using LogLens.Viewer.Store;
using Xunit;

namespace LogLens.Viewer.Tests;

public class QueryParserTests
{
    private static StoredEvent Ev(string msg = "Payment failed for order", LogLevel level = LogLevel.Error, string? cat = "Shop.Orders", string? ex = null, params (string, string)[] props)
        => new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = level,
            Message = msg,
            Template = msg,
            Category = cat,
            Exception = ex,
            Properties = props.ToDictionary(p => p.Item1, p => p.Item2),
            Source = "http",
        };

    private static bool Match(string q, StoredEvent e) => QueryParser.Parse(q)(e);

    [Fact]
    public static void Empty_query_matches_everything()
    {
        Assert.True(Match("", Ev()));
        Assert.True(Match(null!, Ev()));
        Assert.True(Match("   ", Ev()));
    }

    [Fact]
    public static void Free_text_is_case_insensitive_and_searches_all_fields()
    {
        Assert.True(Match("PAYMENT", Ev()));
        Assert.True(Match("orders", Ev()));                        // category
        Assert.True(Match("timeout", Ev(ex: "Gateway timeout")));   // exception
        Assert.True(Match("ann", Ev(props: ("User", "ann"))));      // property value
        Assert.False(Match("refund", Ev()));
        Assert.True(Match("payment order", Ev()));                  // AND
        Assert.False(Match("payment refund", Ev()));
    }

    [Fact]
    public static void Quoted_phrases_and_negation()
    {
        Assert.True(Match("\"failed for\"", Ev()));
        Assert.False(Match("\"for failed\"", Ev()));
        Assert.False(Match("-payment", Ev()));
        Assert.True(Match("-refund", Ev()));
        Assert.True(Match("-level:warning", Ev()));
        Assert.Equal(new[] { "a b", "c", "-\"d e\"" }, QueryParser.Tokenize("\"a b\" c -\"d e\"").Select(t => t.Replace("\"", "")).Take(2).Append("-\"d e\""));
    }

    [Fact]
    public static void Level_terms()
    {
        Assert.True(Match("level:error", Ev()));
        Assert.True(Match("level:err", Ev()));
        Assert.False(Match("level:warning", Ev()));
        Assert.True(Match("level:warning+", Ev()));
        Assert.True(Match("level:warn+", Ev(level: LogLevel.Fatal)));
        Assert.False(Match("level:error+", Ev(level: LogLevel.Information)));
        Assert.True(Match("level:", Ev()));
    }

    [Fact]
    public static void Category_wildcards()
    {
        Assert.True(Match("category:Shop.Orders", Ev()));
        Assert.True(Match("category:shop.orders", Ev()));
        Assert.False(Match("category:Orders", Ev()));
        Assert.True(Match("category:*Orders", Ev()));
        Assert.True(Match("category:Shop*", Ev()));
        Assert.True(Match("category:*op.Or*", Ev()));
        Assert.True(Match("category:*", Ev()));
        Assert.False(Match("category:Shop", Ev(cat: null)));
    }

    [Fact]
    public static void Property_operators()
    {
        var e = Ev(props: [("OrderId", "1002"), ("User", "Ann Smith"), ("Elapsed", "812.5")]);
        Assert.True(Match("OrderId=1002", e));
        Assert.True(Match("orderid=1002", e));
        Assert.False(Match("OrderId=1003", e));
        Assert.True(Match("OrderId!=1003", e));
        Assert.True(Match("Missing!=1", e));
        Assert.True(Match("User~smith", e));
        Assert.True(Match("User=\"Ann Smith\"", e));
        Assert.True(Match("Elapsed>500", e));
        Assert.True(Match("Elapsed>=812.5", e));
        Assert.False(Match("Elapsed<500", e));
        Assert.True(Match("Elapsed<=1000", e));
        Assert.False(Match("User>5", e));          // non-numeric value never matches numeric compare
        Assert.True(Match("has:OrderId", e));
        Assert.True(Match("has:orderid", e));
        Assert.False(Match("has:Nope", e));
        Assert.False(Match("has:exception", e));
        Assert.True(Match("has:exception", Ev(ex: "boom")));
        Assert.True(Match("category=Shop.Orders", e));
    }

    [Fact]
    public static void Field_scoped_text_and_source()
    {
        Assert.True(Match("message:payment", Ev()));
        Assert.False(Match("message:orders", Ev()));
        Assert.True(Match("exception:time", Ev(ex: "timeout")));
        Assert.True(Match("source:http", Ev()));
    }

    [Fact]
    public static void Malformed_terms_degrade_to_text_and_never_throw()
    {
        Assert.True(Match("=", Ev(msg: "a = b")));
        Assert.True(Match("Elapsed>abc", Ev(msg: "Elapsed>abc happened")));
        Assert.False(Match("unknown:thing", Ev()));
        Assert.True(Match("unknown:thing", Ev(msg: "unknown:thing")));
        Assert.True(Match("-", Ev(msg: "a - b")));
        Assert.True(Match("\"unterminated", Ev(msg: "unterminated quote")));
    }
}

using LogLens.Templates;
using Xunit;

namespace LogLens.Tests;

public class MessageTemplateTests
{
    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("", "")]
    [InlineData("{{literal}}", "{literal}")]
    [InlineData("unterminated {Name", "unterminated {Name")]
    [InlineData("{}", "{}")]
    [InlineData("{ spaced }", "{ spaced }")]
    public static void Renders_text_without_holes(string template, string expected)
        => Assert.Equal(expected, MessageTemplate.Parse(template).Render([]));

    [Fact]
    public static void Binds_positional_args_to_named_holes()
    {
        var t = MessageTemplate.Parse("User {UserId} from {Ip}");
        Assert.Equal("User 42 from 10.0.0.1", t.Render([42, "10.0.0.1"]));
        Assert.Equal(new[] { "UserId", "Ip" }, t.PlaceholderNames);
    }

    [Fact]
    public static void Unbound_holes_render_literally_and_extra_args_are_ignored()
    {
        var t = MessageTemplate.Parse("{A} and {B}");
        Assert.Equal("1 and {B}", t.Render([1]));
        Assert.Equal("1 and 2", t.Render([1, 2, 3]));
    }

    [Fact]
    public static void Applies_format_and_alignment()
    {
        Assert.Equal("3.14", MessageTemplate.Parse("{Pi:0.00}").Render([Math.PI]));
        Assert.Equal("[   42]", MessageTemplate.Parse("[{N,5}]").Render([42]));
        Assert.Equal("[42   ]", MessageTemplate.Parse("[{N,-5}]").Render([42]));
        Assert.Equal("[ 3.1]", MessageTemplate.Parse("[{Pi,4:0.0}]").Render([Math.PI]));
        Assert.Equal("[toolong]", MessageTemplate.Parse("[{S,3}]").Render(["toolong"]));
    }

    [Fact]
    public static void Uses_invariant_culture_by_default()
        => Assert.Equal("1234.5", MessageTemplate.Parse("{V}").Render([1234.5]));

    [Fact]
    public static void Null_renders_as_null_literal()
        => Assert.Equal("v=null", MessageTemplate.Parse("v={V}").Render([null]));

    [Fact]
    public static void Destructuring_prefixes_are_stripped_from_names()
    {
        var t = MessageTemplate.Parse("{@Obj} {$Str}");
        Assert.Equal(new[] { "Obj", "Str" }, t.PlaceholderNames);
    }

    [Fact]
    public static void Repeated_name_binds_to_same_arg()
        => Assert.Equal("a a", MessageTemplate.Parse("{X} {X}").Render(["a"]));

    [Fact]
    public static void Invalid_alignment_is_literal_text()
        => Assert.Equal("{Name,abc}", MessageTemplate.Parse("{Name,abc}").Render(["x"]));

    [Fact]
    public static void Parse_is_cached()
        => Assert.Same(MessageTemplate.Parse("cache {Me}"), MessageTemplate.Parse("cache {Me}"));

    [Fact]
    public static void BindProperties_maps_names_to_args()
    {
        var dict = new Dictionary<string, object?>();
        MessageTemplate.Parse("{A} {B}").BindProperties([1], dict);
        Assert.Equal(1, dict["A"]);
        Assert.False(dict.ContainsKey("B"));
    }
}

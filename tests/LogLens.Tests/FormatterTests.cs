using LogLens.Formatting;
using Xunit;

namespace LogLens.Tests;

public class FormatterTests
{
    private static readonly DateTimeOffset Ts = new(2026, 8, 30, 14, 5, 6, 789, TimeSpan.FromHours(2));

    private static string Run(ILogFormatter f, LogEvent e)
    {
        using var sw = new StringWriter();
        f.Format(e, sw);
        return sw.ToString();
    }

    [Fact]
    public static void TextFormatter_default_template()
    {
        var e = new LogEvent(Ts, LogLevel.Warning, "Disk {Pct}% full", [91]);
        Assert.Equal("2026-08-30 14:05:06.789 +02:00 [WRN] Disk 91% full" + Environment.NewLine, Run(new TextFormatter(), e));
    }

    [Fact]
    public static void TextFormatter_custom_template_with_level_formats_and_properties()
    {
        var props = new Dictionary<string, object?> { ["App"] = "svc", ["Pct"] = 91 };
        var e = new LogEvent(Ts, LogLevel.Error, "m", null, props, null, "Cat");
        var text = Run(new TextFormatter("{Level:u}|{Level:l}|{Level}|{Category}|{App}|{Missing}|{Properties}"), e);
        Assert.Equal("ERROR|error|Error|Cat|svc||{App=svc, Pct=91}" + Environment.NewLine, text);
    }

    [Fact]
    public static void TextFormatter_appends_exception()
    {
        var e = new LogEvent(Ts, LogLevel.Error, "boom", exception: new InvalidOperationException("bad"));
        var text = Run(new TextFormatter(), e);
        Assert.Contains("[ERR] boom", text);
        Assert.Contains("InvalidOperationException: bad", text);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Fatal, "FTL")]
    [InlineData(LogLevel.None, "OFF")]
    public static void ShortLevelName(LogLevel level, string expected) => Assert.Equal(expected, TextFormatter.ShortLevelName(level));

    [Fact]
    public static void JsonFormatter_emits_one_escaped_line()
    {
        var props = new Dictionary<string, object?> { ["S"] = "a\"b\\c\n", ["N"] = 1.5, ["B"] = true, ["Null"] = null, ["D"] = Ts };
        var e = new LogEvent(Ts, LogLevel.Information, "say {S}", ["a\"b\\c\n"], props, new Exception("x"), "Cat");
        var line = Run(new JsonFormatter(), e);

        Assert.EndsWith(Environment.NewLine, line);
        Assert.DoesNotContain('\n', line.TrimEnd());
        Assert.Contains("\"timestamp\":\"2026-08-30T14:05:06.7890000+02:00\"", line);
        Assert.Contains("\"level\":\"Information\"", line);
        Assert.Contains("\"message\":\"say a\\\"b\\\\c\\n\\u0001\"", line);
        Assert.Contains("\"template\":\"say {S}\"", line);
        Assert.Contains("\"category\":\"Cat\"", line);
        Assert.Contains("\"exception\":\"System.Exception: x\"", line);
        Assert.Contains("\"S\":\"a\\\"b\\\\c\\n\\u0001\"", line);
        Assert.Contains("\"N\":1.5", line);
        Assert.Contains("\"B\":true", line);
        Assert.Contains("\"Null\":null", line);
        Assert.Contains("\"D\":\"2026-08-30T14:05:06.7890000+02:00\"", line);
        Assert.StartsWith("{", line);
        Assert.EndsWith("}" + Environment.NewLine, line);
    }

    [Fact]
    public static void JsonFormatter_can_omit_rendered_message()
    {
        var e = new LogEvent(Ts, LogLevel.Information, "m");
        Assert.DoesNotContain("\"message\"", Run(new JsonFormatter(renderMessage: false), e));
    }

    [Fact]
    public static void LogfmtFormatter_emits_one_escaped_line()
    {
        var props = new Dictionary<string, object?>
        {
            ["S"] = "a b",
            ["Q"] = "say \"hi\"",
            ["N"] = 1.5,
            ["B"] = true,
            ["Null"] = null,
            ["D"] = Ts,
            ["bad key="] = "x",
        };
        var e = new LogEvent(Ts, LogLevel.Warning, "Disk {Pct}% full", [91], props, new Exception("x\ny"), "Cat");
        var line = Run(new LogfmtFormatter(), e);

        Assert.EndsWith(Environment.NewLine, line);
        Assert.DoesNotContain('\n', line.TrimEnd());
        Assert.StartsWith("ts=2026-08-30T14:05:06.7890000+02:00 level=warn ", line);
        Assert.Contains("msg=\"Disk 91% full\"", line);
        Assert.Contains(" category=Cat", line);
        Assert.Contains(" exception=\"System.Exception: x\\ny\"", line);
        Assert.Contains(" S=\"a b\"", line);
        Assert.Contains(" Q=\"say \\\"hi\\\"\"", line);
        Assert.Contains(" N=1.5", line);
        Assert.Contains(" B=true", line);
        Assert.Contains(" Null=", line);
        Assert.Contains(" D=2026-08-30T14:05:06.7890000+02:00", line);
        Assert.Contains(" bad_key_=x", line);
    }

    [Fact]
    public static void LogfmtFormatter_can_include_template()
    {
        var e = new LogEvent(Ts, LogLevel.Information, "{S}", ["hi"]);
        var line = Run(new LogfmtFormatter(includeTemplate: true), e);
        Assert.Contains(" level=info", line);
        Assert.Contains(" msg=hi", line);
        Assert.Contains(" template={S}", line);
        Assert.DoesNotContain(" template=", Run(new LogfmtFormatter(), e));
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trace")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Fatal, "fatal")]
    [InlineData(LogLevel.None, "none")]
    public static void LogfmtLevelName(LogLevel level, string expected) => Assert.Equal(expected, LogfmtFormatter.LevelName(level));
}

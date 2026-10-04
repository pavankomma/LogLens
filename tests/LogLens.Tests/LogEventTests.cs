using Xunit;

namespace LogLens.Tests;

public class LogEventTests
{
    [Fact]
    public static void Defaults_are_empty_not_null()
    {
        var e = new LogEvent(DateTimeOffset.UnixEpoch, LogLevel.Information, "hi");
        Assert.Empty(e.Args);
        Assert.Empty(e.Properties);
        Assert.Null(e.Exception);
        Assert.Null(e.Category);
        Assert.Equal("hi", e.RenderMessage());
    }

    [Fact]
    public static void RenderMessage_uses_args_and_is_cached()
    {
        var e = new LogEvent(DateTimeOffset.UnixEpoch, LogLevel.Information, "x={X}", [1]);
        Assert.Equal("x=1", e.RenderMessage());
        Assert.Same(e.RenderMessage(), e.RenderMessage());
    }

    [Fact]
    public static void Null_template_throws()
        => Assert.Throws<ArgumentNullException>(() => new LogEvent(DateTimeOffset.UnixEpoch, LogLevel.Information, null!));
}

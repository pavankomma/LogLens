using LogLens.Configuration;
using LogLens.Formatting;
using LogLens.Sinks;
using Xunit;

namespace LogLens.Tests;

public class SinkTests
{
    private static LogEvent Event(string msg = "m", LogLevel level = LogLevel.Information)
        => new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), level, msg);

    [Fact]
    public static void InMemorySink_captures_and_clears()
    {
        var s = new InMemorySink();
        s.Emit(Event("a"));
        s.Emit(Event("b"));
        Assert.Equal(2, s.Events.Count);
        s.Clear();
        Assert.Empty(s.Events);
        s.Flush();
        s.Dispose();
    }

    [Fact]
    public static void TextWriterSink_writes_formatted_lines()
    {
        var sw = new StringWriter();
        using var s = new TextWriterSink(sw, new TextFormatter("{Message}"));
        s.Emit(Event("hello"));
        s.Flush();
        Assert.Equal("hello" + Environment.NewLine, sw.ToString());
    }

    [Fact]
    public static void TextWriterSink_owns_writer_when_asked()
    {
        var sw = new StringWriter();
        new TextWriterSink(sw, ownsWriter: true).Dispose();
        Assert.Throws<ObjectDisposedException>(() => sw.Write("x"));
    }

    [Fact]
    public static void AggregateSink_isolates_failures_and_forwards_lifecycle()
    {
        var good = new InMemorySink();
        var log = new StringWriter();
        SelfLog.Output = log;
        try
        {
            using var agg = new AggregateSink([new ThrowingSink(), good]);
            agg.Emit(Event());
            agg.Flush();
            Assert.Single(good.Events);
            Assert.Contains("failed to emit", log.ToString());
            Assert.Contains("failed to flush", log.ToString());
        }
        finally
        {
            SelfLog.Output = null;
        }
    }

    [Fact]
    public static void ConsoleSink_plain_and_ansi_and_stderr_routing()
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var plain = new ConsoleSink(new TextFormatter("{Message}"), ConsoleTheme.None, errorsToStdErr: true, o, e);
        plain.Emit(Event("info"));
        plain.Emit(Event("err", LogLevel.Error));
        plain.Dispose();
        Assert.Equal("info" + Environment.NewLine, o.ToString());
        Assert.Equal("err" + Environment.NewLine, e.ToString());

        var ansiOut = new StringWriter();
        var ansi = new ConsoleSink(new TextFormatter("{Message}"), ConsoleTheme.Ansi, false, ansiOut, e);
        ansi.Emit(Event("w", LogLevel.Warning));
        Assert.Equal("[33mw" + Environment.NewLine + "[0m", ansiOut.ToString());
    }

    [Fact]
    public static void DebugSink_does_not_throw()
    {
        using var s = new DebugSink();
        s.Emit(Event());
        s.Flush();
    }

    [Fact]
    public static void SelfLog_never_throws_and_formats_exception()
    {
        SelfLog.Output = new ThrowingWriter();
        SelfLog.Write("x");
        var sw = new StringWriter();
        SelfLog.Output = sw;
        SelfLog.Write("ctx", new Exception("inner"));
        SelfLog.Output = null;
        Assert.Contains("ctx: System.Exception: inner", sw.ToString());
        SelfLog.Write("discarded");
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Emit(LogEvent logEvent) => throw new InvalidOperationException();
        public void Flush() => throw new InvalidOperationException();
        public void Dispose() => throw new InvalidOperationException();
    }

    private sealed class ThrowingWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) => throw new IOException();
    }
}

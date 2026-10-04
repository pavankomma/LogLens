using LogLens.Sinks;
using Xunit;

namespace LogLens.Tests;

public class LogLensTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (LogLensLogger Logger, InMemorySink Sink) Create(LogLevel min = LogLevel.Trace)
    {
        var sink = new InMemorySink();
        return (new LogLensLogger(sink, min, () => Fixed), sink);
    }

    [Fact]
    public static void Respects_minimum_level()
    {
        var (logger, sink) = Create(LogLevel.Warning);
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.False(logger.IsEnabled(LogLevel.None));
        logger.Info("dropped");
        logger.Warn("kept");
        Assert.Single(sink.Events);
        Assert.Equal("kept", sink.Events[0].MessageTemplate);
    }

    [Fact]
    public static void Binds_named_args_into_properties_and_uses_clock()
    {
        var (logger, sink) = Create();
        logger.Info("User {UserId} did {Action}", 7, "login");
        var e = Assert.Single(sink.Events);
        Assert.Equal(Fixed, e.Timestamp);
        Assert.Equal(7, e.Properties["UserId"]);
        Assert.Equal("login", e.Properties["Action"]);
        Assert.Equal("User 7 did login", e.RenderMessage());
    }

    [Fact]
    public static void All_level_helpers_map_correctly()
    {
        var (logger, sink) = Create();
        var ex = new Exception("e");
        logger.Trace("t"); logger.Debug("d"); logger.Info("i"); logger.Warn("w"); logger.Error("e"); logger.Fatal("f");
        logger.Trace(ex, "t"); logger.Debug(ex, "d"); logger.Info(ex, "i"); logger.Warn(ex, "w"); logger.Error(ex, "e"); logger.Fatal(ex, "f");
        logger.Info("{A}{B}", 1, 2); logger.Info("{A}{B}{C}", 1, 2, 3); logger.Info("{A}{B}{C}{D}", 1, 2, 3, 4);

        var levels = sink.Events.Take(6).Select(e => e.Level).ToArray();
        Assert.Equal(new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Fatal }, levels);
        Assert.All(sink.Events.Skip(6).Take(6), e => Assert.Same(ex, e.Exception));
        Assert.Equal("1234", sink.Events[^1].RenderMessage());
    }

    [Fact]
    public static void ForContext_and_scopes_enrich_events_inner_wins()
    {
        var (logger, sink) = Create();
        var ctx = logger.ForContext("App", "svc").ForContext("K", "ctx");
        using (ctx.BeginScope("Req", 1))
        using (ctx.BeginScope("K", "scope"))
        {
            ctx.Info("in");
        }

        ctx.Info("out");

        Assert.Equal("svc", sink.Events[0].Properties["App"]);
        Assert.Equal(1, sink.Events[0].Properties["Req"]);
        Assert.Equal("scope", sink.Events[0].Properties["K"]);
        Assert.False(sink.Events[1].Properties.ContainsKey("Req"));
        Assert.Equal("ctx", sink.Events[1].Properties["K"]);
    }

    [Fact]
    public static void ForCategory_sets_category()
    {
        var (logger, sink) = Create();
        logger.ForCategory<LogLensTests>().Info("x");
        logger.ForCategory("Manual").Info("y");
        Assert.Equal(typeof(LogLensTests).FullName, sink.Events[0].Category);
        Assert.Equal("Manual", sink.Events[1].Category);
    }

    [Fact]
    public static void Sink_failure_never_reaches_caller_and_goes_to_selflog()
    {
        var sw = new StringWriter();
        SelfLog.Output = sw;
        try
        {
            var logger = new LogLensLogger(new ThrowingSink());
            logger.Info("boom");
            Assert.Contains("kaboom", sw.ToString());
        }
        finally
        {
            SelfLog.Output = null;
        }
    }

    [Fact]
    public static void Dispose_disposes_sink_only_from_owner()
    {
        var sink = new DisposableSink();
        var logger = new LogLensLogger(sink);
        var child = (LogLensLogger)logger.ForContext("a", 1);
        child.Dispose();
        Assert.False(sink.Disposed);
        logger.Dispose();
        Assert.True(sink.Disposed);
        Assert.False(logger.IsEnabled(LogLevel.Fatal));
    }

    [Fact]
    public static void Null_template_and_args_are_tolerated()
    {
        var (logger, sink) = Create();
        logger.Write(LogLevel.Information, null, null!, null!);
        Assert.Equal(string.Empty, Assert.Single(sink.Events).RenderMessage());
    }

    [Fact]
    public static void None_logger_is_silent() => Assert.False(LogLensLogger.None.IsEnabled(LogLevel.Fatal));

    private sealed class ThrowingSink : ILogSink
    {
        public void Emit(LogEvent logEvent) => throw new InvalidOperationException("kaboom");
        public void Flush() { }
        public void Dispose() { }
    }

    private sealed class DisposableSink : ILogSink
    {
        public bool Disposed { get; private set; }
        public void Emit(LogEvent logEvent) { }
        public void Flush() { }
        public void Dispose() => Disposed = true;
    }
}

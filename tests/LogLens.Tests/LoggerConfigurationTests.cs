using LogLens.Configuration;
using LogLens.Formatting;
using LogLens.Sinks;
using Xunit;

namespace LogLens.Tests;

public class LoggerConfigurationTests
{
    [Fact]
    public static void Builds_pipeline_with_enrichment_level_and_multiple_sinks()
    {
        var a = new InMemorySink();
        var b = new InMemorySink();
        var fixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var logger = new LoggerConfiguration()
            .MinimumLevel(LogLevel.Debug)
            .Enrich("App", "test")
            .Clock(() => fixedTime)
            .WriteTo.InMemory(a)
            .WriteTo.Sink(b)
            .CreateLogger();

        Assert.Equal(LogLevel.Debug, logger.MinimumLevel);
        logger.Trace("no");
        logger.Debug("yes");
        Assert.Single(a.Events);
        Assert.Single(b.Events);
        Assert.Equal("test", a.Events[0].Properties["App"]);
        Assert.Equal(fixedTime, a.Events[0].Timestamp);
    }

    [Fact]
    public static void Async_wraps_sinks_and_dispose_drains()
    {
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration().Async(queueLimit: 10).WriteTo.InMemory(sink).CreateLogger();
        for (var i = 0; i < 5; i++) logger.Info("x");
        logger.Flush();
        Assert.Equal(5, sink.Events.Count);
        logger.Dispose();
    }

    [Fact]
    public static void Empty_configuration_creates_working_logger()
    {
        using var logger = new LoggerConfiguration().CreateLogger();
        logger.Info("nowhere");
    }

    [Fact]
    public static void Registers_builtin_sinks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LogLens.Tests", Guid.NewGuid().ToString("N"));
        var sw = new StringWriter();
        using (var logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Console(new JsonFormatter())
            .WriteTo.Debug()
            .WriteTo.TextWriter(sw, new TextFormatter("{Message}"))
            .WriteTo.File(Path.Combine(dir, "a-.log"), RollingInterval.Day)
            .WriteTo.File(Path.Combine(dir, "b-.json"), new JsonFormatter(), RollingInterval.Day)
            .CreateLogger())
        {
            logger.Info("hello");
        }

        Assert.Equal("hello" + Environment.NewLine, sw.ToString());
        Assert.Equal(2, Directory.GetFiles(dir).Length);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public static void Per_sink_minimum_level_filters_independently()
    {
        var all = new InMemorySink();
        var errorsOnly = new InMemorySink();
        using var logger = new LoggerConfiguration().MinimumLevel(LogLevel.Debug)
            .WriteTo.InMemory(all)
            .WriteTo.Sink(errorsOnly, LogLevel.Error)
            .CreateLogger();
        logger.Debug("d"); logger.Info("i"); logger.Error("e"); logger.Fatal("f");
        Assert.Equal(4, all.Events.Count);
        Assert.Equal(new[] { "e", "f" }, errorsOnly.Events.Select(e => e.MessageTemplate));

        var filter = new LevelFilterSink(errorsOnly, LogLevel.Warning);
        Assert.Same(errorsOnly, filter.Inner);
        Assert.Equal(LogLevel.Warning, filter.MinimumLevel);
        Assert.Throws<ArgumentNullException>(() => new LevelFilterSink(null!, LogLevel.Error));
    }

    [Fact]
    public static void Validates_arguments()
    {
        Assert.Throws<ArgumentException>(() => new LoggerConfiguration().Enrich("", 1));
        Assert.Throws<ArgumentNullException>(() => new LoggerConfiguration().Clock(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoggerConfiguration().Async(0));
        Assert.Throws<ArgumentNullException>(() => new LoggerConfiguration().AddSink(null!));
    }
}

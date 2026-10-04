using LogLens.Configuration;
using LogLens.Sinks;
using Xunit;

namespace LogLens.Tests;

[Collection("LogFacade")]
public class LogFacadeTests
{
    [Fact]
    public static void Facade_forwards_to_logger_and_CloseAndFlush_resets()
    {
        var sink = new InMemorySink();
        Log.Logger = new LoggerConfiguration().MinimumLevel(LogLevel.Trace).WriteTo.InMemory(sink).Async().CreateLogger();
        var ex = new Exception("e");

        Assert.True(Log.IsEnabled(LogLevel.Trace));
        Log.Trace("t"); Log.Debug("d"); Log.Info("i"); Log.Warn("w"); Log.Error("e"); Log.Fatal("f");
        Log.Trace(ex, "t"); Log.Debug(ex, "d"); Log.Info(ex, "i"); Log.Warn(ex, "w"); Log.Error(ex, "e"); Log.Fatal(ex, "f");
        using (Log.BeginScope("S", 1)) Log.ForContext("C", 2).Info("ctx {A}", 3);

        Log.CloseAndFlush();
        Assert.Equal(13, sink.Events.Count);
        Assert.Equal(1, sink.Events[12].Properties["S"]);
        Assert.Equal(2, sink.Events[12].Properties["C"]);
        Assert.Equal(3, sink.Events[12].Properties["A"]);
        Assert.Same(LogLensLogger.None, Log.Logger);
        Log.CloseAndFlush(); // idempotent
        Log.Info("dropped");
        Assert.Equal(13, sink.Events.Count);
    }

    [Fact]
    public static void Null_assignment_falls_back_to_silent_logger()
    {
        Log.Logger = null!;
        Assert.Same(LogLensLogger.None, Log.Logger);
        Assert.False(Log.IsEnabled(LogLevel.Fatal));
    }
}

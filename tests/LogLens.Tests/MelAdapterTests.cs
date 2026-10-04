using LogLens.Configuration;
using LogLens.Extensions.Logging;
using LogLens.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace LogLens.Tests;

[Collection("LogFacade")]
public class MelAdapterTests
{
    [Fact]
    public static void Routes_MEL_events_with_template_properties_scopes_and_category()
    {
        var sink = new InMemorySink();
        using var target = new LoggerConfiguration().MinimumLevel(LogLevel.Trace).WriteTo.InMemory(sink).CreateLogger();
        using var provider = new LogLensProvider(target);
        var mel = provider.CreateLogger("My.Category");
        Assert.Same(mel, provider.CreateLogger("My.Category"));

        Assert.True(mel.IsEnabled(MelLogLevel.Trace));
        using (mel.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "r1", ["{OriginalFormat}"] = "ignored" }))
        using (mel.BeginScope("plain"))
        {
            mel.LogInformation(new EventId(7), "User {UserId} did {Action}", 42, "login");
        }

        mel.LogCritical(new InvalidOperationException("x"), "Crash");
        mel.Log(MelLogLevel.Warning, 0, new CustomState(), null, (s, _) => "custom");

        var e = sink.Events[0];
        Assert.Equal(LogLevel.Information, e.Level);
        Assert.Equal("User {UserId} did {Action}", e.MessageTemplate);
        Assert.Equal("User 42 did login", e.RenderMessage());
        Assert.Equal(42, e.Properties["UserId"]);
        Assert.Equal("My.Category", e.Properties["Category"]);
        Assert.Equal(7, e.Properties["EventId"]);
        Assert.Equal("r1", e.Properties["RequestId"]);
        Assert.Equal("plain", e.Properties["Scope"]);

        Assert.Equal(LogLevel.Fatal, sink.Events[1].Level);
        Assert.IsType<InvalidOperationException>(sink.Events[1].Exception);
        Assert.False(sink.Events[1].Properties.ContainsKey("RequestId"));

        Assert.Equal("custom", sink.Events[2].RenderMessage());
        Assert.Equal(LogLevel.Warning, sink.Events[2].Level);
    }

    [Theory]
    [InlineData(MelLogLevel.Trace, LogLevel.Trace)]
    [InlineData(MelLogLevel.Debug, LogLevel.Debug)]
    [InlineData(MelLogLevel.Information, LogLevel.Information)]
    [InlineData(MelLogLevel.Warning, LogLevel.Warning)]
    [InlineData(MelLogLevel.Error, LogLevel.Error)]
    [InlineData(MelLogLevel.Critical, LogLevel.Fatal)]
    [InlineData(MelLogLevel.None, LogLevel.None)]
    public static void Level_mapping(MelLogLevel mel, LogLevel expected) => Assert.Equal(expected, LogLensAdapter.ToLensLevel(mel));

    [Fact]
    public static void AddLogLens_registers_provider_via_DI()
    {
        var sink = new InMemorySink();
        var target = new LoggerConfiguration().WriteTo.InMemory(sink).CreateLogger();
        using var services = new ServiceCollection()
            .AddLogging(b => b.AddLogLens(target, dispose: true))
            .BuildServiceProvider();

        services.GetRequiredService<ILogger<MelAdapterTests>>().LogInformation("from DI {N}", 1);
        Assert.Same(target, services.GetRequiredService<ILogLensLogger>());
        Assert.Single(sink.Events);
        Assert.Equal(typeof(MelAdapterTests).FullName, sink.Events[0].Properties["Category"]);
    }

    [Fact]
    public static void Provider_without_logger_uses_Log_facade_at_call_time()
    {
        var sink = new InMemorySink();
        Log.Logger = new LoggerConfiguration().WriteTo.InMemory(sink).CreateLogger();
        try
        {
            using var provider = new LogLensProvider();
            var mel = provider.CreateLogger("c");
            mel.LogInformation("late bound");
            using (mel.BeginScope(new Dictionary<string, object?> { ["K"] = 1 })) mel.LogInformation("scoped");
            var ctx = provider.CreateLogger("d");
            ctx.LogInformation("x");
            Assert.Equal(3, sink.Events.Count);
            Assert.Equal(1, sink.Events[1].Properties["K"]);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private sealed class CustomState
    {
        public override string ToString() => "state";
    }
}

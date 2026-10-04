using System.Text;
using LogLens.Extensions.Logging;
using LogLens.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LogLens.Tests;

[Collection("LogFacade")]
public sealed class MelConfigurationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LogLens.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Log.CloseAndFlush();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Options_bind_from_configuration_section_and_build_a_working_logger()
    {
        var json = $$"""
        {
          "Logging": {
            "LogLens": {
              "MinimumLevel": "Debug",
              "Console": false,
              "File": { "Path": "{{_dir.Replace("\\", "/")}}/app-.json", "Rolling": "Infinite", "Retain": 3, "MinimumLevel": "Warning" },
              "Async": false,
              "Enrich": { "App": "cfg-test", "Env": "test" }
            }
          }
        }
        """;
        var config = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var section = config.GetSection("Logging:LogLens");

        var options = new LogLensOptions();
        section.Bind(options);
        Assert.Equal(LogLevel.Debug, options.MinimumLevel);
        Assert.False(options.Console);
        Assert.Equal(LogLevel.Warning, options.File!.MinimumLevel);
        Assert.Equal(3, options.File.Retain);
        Assert.Equal("cfg-test", options.Enrich["App"]);

        using var services = new ServiceCollection().AddLogging(b => b.AddLogLens(section)).BuildServiceProvider();
        var logger = services.GetRequiredService<ILogger<MelConfigurationTests>>();
        logger.LogDebug("debug {N}", 1);
        logger.LogWarning("warn {N}", 2);
        Log.CloseAndFlush();

        var lines = File.ReadAllLines(Path.Combine(_dir, "app-.json"));
        Assert.Single(lines);                       // Debug filtered by the file sink's own minimum level
        Assert.Contains("\"App\":\"cfg-test\"", lines[0]);
        Assert.Contains("warn 2", lines[0]);
    }

    [Fact]
    public void Code_configuration_overload_sets_facade_and_disposes_with_host()
    {
        var sink = new InMemorySink();
        var services = new ServiceCollection().AddLogging(b => b.AddLogLens(cfg => cfg.MinimumLevel(LogLevel.Trace).WriteTo.InMemory(sink))).BuildServiceProvider();
        services.GetRequiredService<ILogger<MelConfigurationTests>>().LogInformation("via code");
        Assert.Single(sink.Events);
        Assert.NotSame(global::LogLens.LogLensLogger.None, Log.Logger);
        services.Dispose();
    }

    [Fact]
    public void Options_create_viewer_sink_when_url_present()
    {
        var o = new LogLensOptions { Viewer = "http://localhost:5341", ApiKey = "k", Async = false, Console = false };
        using var logger = o.CreateLogger();
        Assert.Equal(LogLevel.Information, logger.MinimumLevel);
        Assert.Throws<ArgumentNullException>(() => global::LogLens.Extensions.Logging.LoggingBuilderExtensions.AddLogLens(null!, (IConfiguration)null!));
    }
}

using System.Net.Http;
using LogLens.Configuration;

namespace LogLens.Sinks.Http;

/// <summary>Adds <c>.WriteTo.Http(...)</c> to <see cref="SinkConfiguration"/>.</summary>
public static class HttpSinkConfigurationExtensions
{
    /// <summary>Sends events to LogLens.Viewer (or a compatible endpoint) at <paramref name="baseUrl"/>.</summary>
    public static LoggerConfiguration Http(
        this SinkConfiguration writeTo,
        string baseUrl,
        string? apiKey = null,
        int batchSize = 100,
        TimeSpan? period = null,
        int queueLimit = 10_000,
        HttpClient? httpClient = null,
        LogLevel restrictedToMinimumLevel = LogLevel.Trace)
    {
        if (writeTo is null) throw new ArgumentNullException(nameof(writeTo));
        return writeTo.Sink(new HttpSink(baseUrl, apiKey, batchSize, period, queueLimit, httpClient), restrictedToMinimumLevel);
    }
}

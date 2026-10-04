using System.Net;
using LogLens.Configuration;
using LogLens.Sinks.Http;
using Xunit;

namespace LogLens.Viewer.Tests;

public class HttpSinkTests
{
    private static LogEvent Ev(int i) => new(DateTimeOffset.UtcNow, LogLevel.Information, "n={N}", [i], new Dictionary<string, object?> { ["N"] = i });

    [Fact]
    public static void Batches_events_as_ndjson_with_api_key()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        using (var sink = new HttpSink("http://viewer.test/", apiKey: "k", batchSize: 3, period: TimeSpan.FromMilliseconds(50), httpClient: client))
        {
            for (var i = 0; i < 7; i++) sink.Emit(Ev(i));
            sink.Flush();
        }

        Assert.Equal(7, handler.Bodies.Sum(b => b.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length));
        Assert.All(handler.Requests, r => Assert.Equal("k", r.Headers.GetValues("X-Api-Key").Single()));
        Assert.All(handler.Requests, r => Assert.Equal("http://viewer.test/api/ingest", r.RequestUri!.ToString()));
        Assert.Contains("\"N\":0", handler.Bodies[0]);
        using var viaConfig = new LoggerConfiguration().WriteTo.Http("http://viewer.test", httpClient: client).CreateLogger();
        viaConfig.Info("via config");
        viaConfig.Flush();
        Assert.Contains(handler.Bodies, b => b.Contains("via config", StringComparison.Ordinal));
    }

    [Fact]
    public static void Retries_server_errors_then_drops_and_reports_to_selflog()
    {
        var log = new StringWriter();
        SelfLog.Output = log;
        try
        {
            var handler = new RecordingHandler(HttpStatusCode.InternalServerError);
            using var client = new HttpClient(handler);
            using var sink = new HttpSink("http://viewer.test", batchSize: 1, period: TimeSpan.FromMilliseconds(20), httpClient: client);
            sink.Emit(Ev(1));
            sink.Flush();
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(1, sink.DroppedEvents);
            Assert.Contains("giving up", log.ToString());
        }
        finally
        {
            SelfLog.Output = null;
        }
    }

    [Fact]
    public static void Client_errors_drop_without_retry_and_exceptions_never_escape()
    {
        var rejected = new RecordingHandler(HttpStatusCode.BadRequest);
        using var c1 = new HttpClient(rejected);
        using (var sink = new HttpSink("http://viewer.test", batchSize: 1, httpClient: c1))
        {
            sink.Emit(Ev(1));
            sink.Flush();
            Assert.Single(rejected.Requests);
            Assert.Equal(1, sink.DroppedEvents);
        }

        using var c2 = new HttpClient(new ThrowingHandler());
        using (var sink = new HttpSink("http://viewer.test", batchSize: 1, period: TimeSpan.FromMilliseconds(10), httpClient: c2))
        {
            sink.Emit(Ev(1));
            sink.Flush();
            Assert.Equal(1, sink.DroppedEvents);
        }
    }

    [Fact]
    public static void Queue_overflow_drops_oldest_and_arguments_are_validated()
    {
        using var client = new HttpClient(new RecordingHandler(HttpStatusCode.OK));
        using var sink = new HttpSink("http://viewer.test", batchSize: 1000, period: TimeSpan.FromHours(1), queueLimit: 2, httpClient: client);
        sink.Emit(Ev(1)); sink.Emit(Ev(2)); sink.Emit(Ev(3));
        Assert.Equal(1, sink.DroppedEvents);

        Assert.Throws<ArgumentException>(() => new HttpSink(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpSink("http://x", batchSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpSink("http://x", queueLimit: 0));
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Bodies) Bodies.Add(body);
            return new HttpResponseMessage(status);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("down");
    }
}

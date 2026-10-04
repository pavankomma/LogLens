using System.Net.Http;
using System.Text;
using LogLens.Formatting;
using LogLens.Sinks;

namespace LogLens.Sinks.Http;

/// <summary>
/// Batches events as NDJSON and POSTs them to <c>{baseUrl}/api/ingest</c> (LogLens.Viewer or any compatible
/// endpoint). Sending happens on a background thread; the sink never throws into callers.
/// </summary>
public sealed class HttpSink : ILogSink
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _endpoint;
    private readonly string? _apiKey;
    private readonly int _batchSize;
    private readonly TimeSpan _period;
    private readonly int _queueLimit;
    private readonly ILogFormatter _formatter = new JsonFormatter();
    private readonly Queue<LogEvent> _queue = new();
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread _worker;
    private volatile bool _stopping;
    private long _dropped;

    /// <summary>Creates an HTTP sink.</summary>
    /// <param name="baseUrl">Viewer base URL, e.g. <c>http://localhost:5341</c>.</param>
    /// <param name="apiKey">Optional value for the <c>X-Api-Key</c> header.</param>
    /// <param name="batchSize">Send when this many events are queued.</param>
    /// <param name="period">Send at least this often while events are queued.</param>
    /// <param name="queueLimit">Maximum queued events; beyond this the oldest are dropped.</param>
    /// <param name="httpClient">Optional client to reuse (not disposed by the sink).</param>
    public HttpSink(string baseUrl, string? apiKey = null, int batchSize = 100, TimeSpan? period = null, int queueLimit = 10_000, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (queueLimit <= 0) throw new ArgumentOutOfRangeException(nameof(queueLimit));

        _endpoint = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/ingest");
        _apiKey = apiKey;
        _batchSize = batchSize;
        _period = period ?? TimeSpan.FromSeconds(1);
        _queueLimit = queueLimit;
        _ownsClient = httpClient is null;
        _client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        _worker = new Thread(Run) { IsBackground = true, Name = "LogLens.HttpSink" };
        _worker.Start();
    }

    /// <summary>Events discarded because the queue was full.</summary>
    public long DroppedEvents => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (_stopping || logEvent is null) return;
        int count;
        lock (_sync)
        {
            if (_queue.Count >= _queueLimit)
            {
                _queue.Dequeue();
                Interlocked.Increment(ref _dropped);
            }

            _queue.Enqueue(logEvent);
            count = _queue.Count;
        }

        if (count >= _batchSize) _wake.Set();
    }

    /// <inheritdoc />
    public void Flush()
    {
        _wake.Set();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (_sync) if (_queue.Count == 0 && !_sending) return;
            Thread.Sleep(20);
        }
    }

    private volatile bool _sending;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_stopping) return;
        _stopping = true;
        _wake.Set();
        _worker.Join(TimeSpan.FromSeconds(10));
        if (_ownsClient) _client.Dispose();
        _wake.Dispose();
    }

    private void Run()
    {
        while (true)
        {
            _wake.Wait(_period);
            _wake.Reset();

            List<LogEvent>? batch;
            lock (_sync)
            {
                batch = _queue.Count == 0 ? null : new List<LogEvent>(_queue.Count);
                if (batch is not null)
                {
                    _sending = true;
                    while (_queue.Count > 0 && batch.Count < _batchSize * 10) batch.Add(_queue.Dequeue());
                }
            }

            if (batch is not null)
            {
                Send(batch);
                lock (_sync) _sending = _queue.Count > 0;
            }
            else if (_stopping)
            {
                return;
            }

            if (_stopping)
            {
                lock (_sync) if (_queue.Count == 0) return;
            }
        }
    }

    internal string Serialize(IEnumerable<LogEvent> events)
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        sw.NewLine = "\n";
        foreach (var e in events) _formatter.Format(e, sw);
        return sb.ToString();
    }

    private void Send(List<LogEvent> batch)
    {
        var body = Serialize(batch);
        var delay = TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson"),
                };
                if (_apiKey is not null) request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
                using var response = _client.SendAsync(request).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode) return;
                if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout)
                {
                    SelfLog.Write($"HttpSink: server rejected batch with {(int)response.StatusCode}; dropping {batch.Count} events.");
                    Interlocked.Add(ref _dropped, batch.Count);
                    return;
                }

                SelfLog.Write($"HttpSink: attempt {attempt} got {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                SelfLog.Write($"HttpSink: attempt {attempt} failed", ex);
            }

            if (attempt < 3 && !_stopping) Thread.Sleep(delay);
            delay = TimeSpan.FromTicks(delay.Ticks * 4);
        }

        Interlocked.Add(ref _dropped, batch.Count);
        SelfLog.Write($"HttpSink: giving up on batch of {batch.Count} events.");
    }
}

using System.Threading.Channels;
using LogLens.Configuration;

namespace LogLens.Sinks;

/// <summary>
/// Wraps a sink with a bounded queue drained by a single background thread so callers never block on I/O.
/// </summary>
public sealed class AsyncSink : ILogSink
{
    private readonly ILogSink _inner;
    private readonly Channel<LogEvent> _channel;
    private readonly Thread _worker;
    private readonly TimeSpan _shutdownTimeout;
    private readonly object _flushSync = new();
    private long _dropped;
    private int _disposed;

    /// <summary>Creates an async wrapper.</summary>
    /// <param name="inner">Sink to drain into.</param>
    /// <param name="queueLimit">Maximum queued events.</param>
    /// <param name="overflow">What to do when the queue is full.</param>
    /// <param name="shutdownTimeout">How long <see cref="Dispose"/> waits for the queue to drain.</param>
    public AsyncSink(ILogSink inner, int queueLimit = 10_000, OverflowPolicy overflow = OverflowPolicy.DropOldest, TimeSpan? shutdownTimeout = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (queueLimit <= 0) throw new ArgumentOutOfRangeException(nameof(queueLimit));
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);

        _channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(queueLimit)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = overflow switch
            {
                OverflowPolicy.Block => BoundedChannelFullMode.Wait,
                OverflowPolicy.DropNewest => BoundedChannelFullMode.DropWrite,
                _ => BoundedChannelFullMode.DropOldest,
            },
        });

        _worker = new Thread(Drain) { IsBackground = true, Name = "LogLens.AsyncSink" };
        _worker.Start();
    }

    /// <summary>Number of events discarded because the queue was full.</summary>
    public long DroppedEvents => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (_disposed != 0) return;
        if (_channel.Writer.TryWrite(logEvent)) return;

        // Blocking mode: TryWrite fails only when full, so wait synchronously.
        try
        {
            _channel.Writer.WriteAsync(logEvent).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Waits until every event queued before this call has been handed to the inner sink, then flushes it.</summary>
    public void Flush()
    {
        lock (_flushSync)
        {
            var marker = new FlushMarker();
            if (_disposed != 0 || !_channel.Writer.TryWrite(marker.Event))
            {
                _inner.Flush();
                return;
            }

            marker.Wait(_shutdownTimeout);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _channel.Writer.TryComplete();
        if (!_worker.Join(_shutdownTimeout))
            SelfLog.Write("AsyncSink: shutdown timeout elapsed before the queue drained.");
        _inner.Dispose();
    }

    private void Drain()
    {
        var reader = _channel.Reader;
        try
        {
            while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var logEvent))
                {
                    if (FlushMarker.TryGet(logEvent, out var marker))
                    {
                        try { _inner.Flush(); }
                        catch (Exception ex) { SelfLog.Write("AsyncSink: inner flush failed", ex); }
                        marker.Signal();
                        continue;
                    }

                    try { _inner.Emit(logEvent); }
                    catch (Exception ex) { SelfLog.Write("AsyncSink: inner emit failed", ex); }
                }
            }
        }
        catch (Exception ex)
        {
            SelfLog.Write("AsyncSink: drain loop terminated", ex);
        }
    }

    /// <summary>A sentinel event that carries a completion signal through the queue.</summary>
    private sealed class FlushMarker
    {
        private static readonly Dictionary<LogEvent, FlushMarker> Markers = [];
        private readonly ManualResetEventSlim _done = new(false);

        public FlushMarker()
        {
            Event = new LogEvent(DateTimeOffset.MinValue, LogLevel.None, string.Empty);
            lock (Markers) Markers[Event] = this;
        }

        public LogEvent Event { get; }

        public static bool TryGet(LogEvent e, out FlushMarker marker)
        {
            if (e.Level != LogLevel.None)
            {
                marker = null!;
                return false;
            }

            lock (Markers)
            {
                if (!Markers.TryGetValue(e, out marker!)) return false;
                Markers.Remove(e);
                return true;
            }
        }

        public void Signal() => _done.Set();

        public void Wait(TimeSpan timeout)
        {
            if (!_done.Wait(timeout)) SelfLog.Write("AsyncSink: flush timed out.");
            _done.Dispose();
        }
    }
}

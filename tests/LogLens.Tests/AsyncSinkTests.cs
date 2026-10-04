using LogLens.Configuration;
using LogLens.Sinks;
using Xunit;

namespace LogLens.Tests;

public class AsyncSinkTests
{
    private static LogEvent Event(int i) => new(DateTimeOffset.UnixEpoch, LogLevel.Information, "e{I}", [i]);

    [Fact]
    public static void Delivers_all_events_in_order_and_flush_waits()
    {
        var inner = new InMemorySink();
        using var async = new AsyncSink(inner, queueLimit: 100);
        for (var i = 0; i < 50; i++) async.Emit(Event(i));
        async.Flush();
        Assert.Equal(50, inner.Events.Count);
        Assert.Equal(Enumerable.Range(0, 50), inner.Events.Select(e => (int)e.Args[0]!));
    }

    [Fact]
    public static void Dispose_drains_queue()
    {
        var inner = new InMemorySink();
        var async = new AsyncSink(inner, queueLimit: 1000);
        for (var i = 0; i < 500; i++) async.Emit(Event(i));
        async.Dispose();
        Assert.Equal(500, inner.Events.Count);
        async.Emit(Event(1)); // after dispose: ignored, no throw
        async.Dispose();      // idempotent
    }

    [Fact]
    public static void DropOldest_and_DropNewest_when_full()
    {
        var gate = new ManualResetEventSlim(false);
        var blocking = new BlockingSink(gate);
        var sink = new AsyncSink(blocking, queueLimit: 2, overflow: OverflowPolicy.DropOldest);
        sink.Emit(Event(0)); // taken by worker, blocks on gate
        SpinWait.SpinUntil(() => blocking.Started, 5000);
        sink.Emit(Event(1));
        sink.Emit(Event(2));
        sink.Emit(Event(3)); // drops 1
        gate.Set();
        sink.Dispose();
        Assert.Equal(new[] { 0, 2, 3 }, blocking.Seen);

        var gate2 = new ManualResetEventSlim(false);
        var blocking2 = new BlockingSink(gate2);
        var sink2 = new AsyncSink(blocking2, queueLimit: 2, overflow: OverflowPolicy.DropNewest);
        sink2.Emit(Event(0));
        SpinWait.SpinUntil(() => blocking2.Started, 5000);
        sink2.Emit(Event(1));
        sink2.Emit(Event(2));
        sink2.Emit(Event(3)); // dropped
        gate2.Set();
        sink2.Dispose();
        Assert.Equal(new[] { 0, 1, 2 }, blocking2.Seen);
    }

    [Fact]
    public static async Task Block_policy_waits_for_space()
    {
        var gate = new ManualResetEventSlim(false);
        var blocking = new BlockingSink(gate);
        var sink = new AsyncSink(blocking, queueLimit: 1, overflow: OverflowPolicy.Block);
        sink.Emit(Event(0));
        SpinWait.SpinUntil(() => blocking.Started, 5000);
        sink.Emit(Event(1)); // fills queue
        var producer = Task.Run(() => sink.Emit(Event(2)));
        Assert.NotSame(producer, await Task.WhenAny(producer, Task.Delay(200)));
        gate.Set();
        await producer.WaitAsync(TimeSpan.FromSeconds(5));
        sink.Dispose();
        Assert.Equal(new[] { 0, 1, 2 }, blocking.Seen);
    }

    [Fact]
    public static void Inner_failures_go_to_selflog_and_do_not_stop_the_drain()
    {
        var log = new StringWriter();
        SelfLog.Output = log;
        try
        {
            var inner = new InMemorySink();
            using var sink = new AsyncSink(new FailOnceSink(inner));
            sink.Emit(Event(0));
            sink.Emit(Event(1));
            sink.Flush();
            Assert.Single(inner.Events);
            Assert.Contains("inner emit failed", log.ToString());
        }
        finally
        {
            SelfLog.Output = null;
        }
    }

    [Fact]
    public static void Invalid_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => new AsyncSink(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncSink(new InMemorySink(), queueLimit: 0));
    }

    private sealed class BlockingSink(ManualResetEventSlim gate) : ILogSink
    {
        private readonly List<int> _seen = [];
        public volatile bool Started;
        public IReadOnlyList<int> Seen => _seen;

        public void Emit(LogEvent logEvent)
        {
            Started = true;
            gate.Wait();
            _seen.Add((int)logEvent.Args[0]!);
        }

        public void Flush() { }
        public void Dispose() { }
    }

    private sealed class FailOnceSink(ILogSink inner) : ILogSink
    {
        private bool _failed;
        public void Emit(LogEvent logEvent)
        {
            if (!_failed) { _failed = true; throw new InvalidOperationException(); }
            inner.Emit(logEvent);
        }

        public void Flush() => inner.Flush();
        public void Dispose() => inner.Dispose();
    }
}

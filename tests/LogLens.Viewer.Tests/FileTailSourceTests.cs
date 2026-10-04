using LogLens.Viewer.Ingest;
using LogLens.Viewer.Store;
using Xunit;

namespace LogLens.Viewer.Tests;

public sealed class FileTailSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LogLens.Viewer.Tests", Guid.NewGuid().ToString("N"));

    public FileTailSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Reads_existing_then_appends_then_new_files_and_handles_partial_lines()
    {
        var file = Path.Combine(_dir, "app-.json");
        File.WriteAllText(file, "{\"message\":\"one\"}\n{\"message\":\"two\"}\n");
        var store = new LogStore(100);
        var tail = new FileTailSource(store, [_dir]);

        Assert.Equal(2, tail.PollOnce());
        Assert.Equal(0, tail.PollOnce());

        using (var w = new StreamWriter(new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
        {
            w.Write("{\"message\":\"thr");
            w.Flush();
            Assert.Equal(0, tail.PollOnce()); // partial line held back
            w.Write("ee\"}\n");
        }

        Assert.Equal(1, tail.PollOnce());

        File.WriteAllText(Path.Combine(_dir, "other.ndjson"), "{\"message\":\"four\"}\n");
        File.WriteAllText(Path.Combine(_dir, "ignored.txt"), "{\"message\":\"nope\"}\n");
        Assert.Equal(1, tail.PollOnce());

        var messages = store.Query(_ => true, null, null, 10, null).Events.Select(e => e.Message).OrderBy(m => m, StringComparer.Ordinal);
        Assert.Equal(new[] { "four", "one", "three", "two" }, messages);
        Assert.Equal(2, tail.TrackedFiles.Count);
        Assert.All(store.Query(_ => true, null, null, 10, null).Events, e => Assert.NotNull(e.Source));
    }

    [Fact]
    public void Truncated_file_is_reread_from_start()
    {
        var file = Path.Combine(_dir, "a.log");
        File.WriteAllText(file, "{\"message\":\"long line number one\"}\n");
        var store = new LogStore(100);
        var tail = new FileTailSource(store, [file]);
        Assert.Equal(1, tail.PollOnce());
        File.WriteAllText(file, "{\"message\":\"x\"}\n");
        Assert.Equal(1, tail.PollOnce());
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public async Task Runs_as_hosted_service()
    {
        var file = Path.Combine(_dir, "h.json");
        var store = new LogStore(100);
        var tail = new FileTailSource(store, [file], TimeSpan.FromMilliseconds(20));
        await tail.StartAsync(CancellationToken.None);
        File.WriteAllText(file, "{\"message\":\"hosted\"}\n");
        using var cts = new CancellationTokenSource();
        var reader = store.Subscribe(cts.Token);
        var got = await reader.ReadAsync(new CancellationTokenSource(5000).Token);
        Assert.Equal("hosted", got.Message);
        await tail.StopAsync(CancellationToken.None);
    }
}

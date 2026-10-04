using System.Text;
using LogLens.Viewer.Store;

namespace LogLens.Viewer.Ingest;

/// <summary>Tails NDJSON files (or every matching file in a directory), ingesting existing content then appends.</summary>
public sealed class FileTailSource : BackgroundService
{
    private static readonly string[] Extensions = [".json", ".ndjson", ".log", ".jsonl"];
    private readonly LogStore _store;
    private readonly IReadOnlyList<string> _paths;
    private readonly TimeSpan _pollInterval;
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StringBuilder> _partial = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a tail source for <paramref name="paths"/> (files or directories).</summary>
    public FileTailSource(LogStore store, IEnumerable<string> paths, TimeSpan? pollInterval = null)
    {
        _store = store;
        _paths = paths.Select(Path.GetFullPath).ToArray();
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>Files currently being followed.</summary>
    public IReadOnlyCollection<string> TrackedFiles { get { lock (_offsets) return _offsets.Keys.ToArray(); } }

    /// <summary>Reads any new data from all tracked files once. Returns events ingested.</summary>
    public int PollOnce()
    {
        var ingested = 0;
        foreach (var file in Enumerate())
        {
            try { ingested += ReadNew(file); }
            catch (IOException ex) { SelfLog.Write($"FileTailSource: {file}", ex); }
            catch (UnauthorizedAccessException ex) { SelfLog.Write($"FileTailSource: {file}", ex); }
        }

        return ingested;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            PollOnce();
            try { await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private IEnumerable<string> Enumerate()
    {
        foreach (var p in _paths)
        {
            if (File.Exists(p)) { yield return p; continue; }
            if (!Directory.Exists(p)) continue;
            foreach (var f in Directory.EnumerateFiles(p))
                if (Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)) yield return f;
        }
    }

    private int ReadNew(string file)
    {
        long offset;
        lock (_offsets) _offsets.TryGetValue(file, out offset);

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < offset) offset = 0; // truncated / rotated in place
        if (stream.Length == offset) return 0;

        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 64 * 1024, leaveOpen: true);
        var text = reader.ReadToEnd();
        var newOffset = stream.Length;

        if (!_partial.TryGetValue(file, out var carry)) _partial[file] = carry = new StringBuilder();
        carry.Append(text);
        var buffered = carry.ToString();
        var lastNewline = buffered.LastIndexOf('\n');
        string complete;
        if (lastNewline < 0) { complete = string.Empty; }
        else
        {
            complete = buffered.Substring(0, lastNewline + 1);
            carry.Clear();
            carry.Append(buffered, lastNewline + 1, buffered.Length - lastNewline - 1);
        }

        lock (_offsets) _offsets[file] = newOffset;
        if (complete.Length == 0) return 0;

        var events = new List<StoredEvent>();
        foreach (var line in complete.Split('\n'))
            if (EventParser.TryParseLine(line, file, null, out var e)) events.Add(e);
        return _store.Add(events);
    }
}

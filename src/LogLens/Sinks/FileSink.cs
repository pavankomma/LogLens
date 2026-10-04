using System.Globalization;
using System.Text;
using LogLens.Configuration;
using LogLens.Formatting;

namespace LogLens.Sinks;

/// <summary>
/// Appends formatted events to a file, rolling by time interval and/or size, and pruning old files.
/// The path <c>logs/app-.log</c> yields <c>logs/app-20260830.log</c> for <see cref="RollingInterval.Day"/>;
/// size rolls append <c>_001</c>, <c>_002</c>… before the extension.
/// </summary>
public sealed class FileSink : ILogSink
{
    private readonly string _directory;
    private readonly string _prefix;
    private readonly string _extension;
    private readonly ILogFormatter _formatter;
    private readonly RollingInterval _interval;
    private readonly long? _sizeLimit;
    private readonly int? _retainedFileCount;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();

    private StreamWriter? _writer;
    private string? _currentPeriod;
    private int _sequence;
    private long _currentSize;
    private DateTimeOffset _nextCheckpoint = DateTimeOffset.MinValue;

    /// <summary>Creates a file sink.</summary>
    /// <param name="path">Base path; the rolling period is inserted before the extension.</param>
    /// <param name="formatter">Formatter; defaults to <see cref="TextFormatter"/>.</param>
    /// <param name="rollingInterval">Time-based rolling.</param>
    /// <param name="fileSizeLimitBytes">Size-based rolling; null for unlimited.</param>
    /// <param name="retainedFileCount">Keep at most this many files matching the pattern; null for unlimited.</param>
    /// <param name="clock">Time source; defaults to <see cref="DateTimeOffset.Now"/>.</param>
    public FileSink(
        string path,
        ILogFormatter? formatter = null,
        RollingInterval rollingInterval = RollingInterval.Infinite,
        long? fileSizeLimitBytes = null,
        int? retainedFileCount = null,
        Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        if (fileSizeLimitBytes is <= 0) throw new ArgumentOutOfRangeException(nameof(fileSizeLimitBytes));
        if (retainedFileCount is <= 0) throw new ArgumentOutOfRangeException(nameof(retainedFileCount));

        var full = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
        _prefix = Path.GetFileNameWithoutExtension(full);
        _extension = Path.GetExtension(full);
        _formatter = formatter ?? new TextFormatter();
        _interval = rollingInterval;
        _sizeLimit = fileSizeLimitBytes;
        _retainedFileCount = retainedFileCount;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Path of the file currently being written, or null before the first event.</summary>
    public string? CurrentFilePath { get; private set; }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null) return;
        lock (_sync)
        {
            var now = _clock();
            EnsureWriter(now);

            var sb = new StringBuilder(128);
            using (var sw = new StringWriter(sb, CultureInfo.InvariantCulture)) _formatter.Format(logEvent, sw);
            var text = sb.ToString();
            var bytes = Encoding.UTF8.GetByteCount(text);

            if (_sizeLimit is { } limit && _currentSize > 0 && _currentSize + bytes > limit)
            {
                _sequence++;
                OpenFile(now);
            }

            _writer!.Write(text);
            _currentSize += bytes;
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_sync) _writer?.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal static string PeriodToken(RollingInterval interval, DateTimeOffset now) => interval switch
    {
        RollingInterval.Year => now.ToString("yyyy", CultureInfo.InvariantCulture),
        RollingInterval.Month => now.ToString("yyyyMM", CultureInfo.InvariantCulture),
        RollingInterval.Day => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        RollingInterval.Hour => now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture),
        RollingInterval.Minute => now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture),
        _ => string.Empty,
    };

    internal static DateTimeOffset NextCheckpoint(RollingInterval interval, DateTimeOffset now)
    {
        var t = now;
        return interval switch
        {
            RollingInterval.Year => new DateTimeOffset(t.Year, 1, 1, 0, 0, 0, t.Offset).AddYears(1),
            RollingInterval.Month => new DateTimeOffset(t.Year, t.Month, 1, 0, 0, 0, t.Offset).AddMonths(1),
            RollingInterval.Day => new DateTimeOffset(t.Year, t.Month, t.Day, 0, 0, 0, t.Offset).AddDays(1),
            RollingInterval.Hour => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset).AddHours(1),
            RollingInterval.Minute => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset).AddMinutes(1),
            _ => DateTimeOffset.MaxValue,
        };
    }

    private void EnsureWriter(DateTimeOffset now)
    {
        if (_writer is not null && now < _nextCheckpoint) return;

        var period = PeriodToken(_interval, now);
        if (_writer is not null && period == _currentPeriod)
        {
            _nextCheckpoint = NextCheckpoint(_interval, now);
            return;
        }

        _currentPeriod = period;
        _nextCheckpoint = NextCheckpoint(_interval, now);
        _sequence = 0;
        OpenFile(now);
    }

    private void OpenFile(DateTimeOffset now)
    {
        _writer?.Dispose();
        Directory.CreateDirectory(_directory);

        // If a size-rolled file already exists from a previous run, skip past it.
        string path;
        while (true)
        {
            path = BuildPath(_currentPeriod ?? PeriodToken(_interval, now), _sequence);
            if (_sizeLimit is null || !File.Exists(path) || new FileInfo(path).Length < _sizeLimit) break;
            _sequence++;
        }

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        _currentSize = stream.Length;
        CurrentFilePath = path;

        ApplyRetention();
    }

    private string BuildPath(string period, int sequence)
    {
        var name = _prefix + period;
        if (sequence > 0) name += "_" + sequence.ToString("000", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, name + _extension);
    }

    private void ApplyRetention()
    {
        if (_retainedFileCount is not { } keep) return;
        try
        {
            var candidates = Directory.GetFiles(_directory, _prefix + "*" + _extension)
                .Where(f => !string.Equals(f, CurrentFilePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase) // period token sorts lexically = chronologically
                .Skip(keep - 1)
                .ToList();
            foreach (var file in candidates)
            {
                try { File.Delete(file); }
                catch (Exception ex) { SelfLog.Write($"FileSink: could not delete {file}", ex); }
            }
        }
        catch (Exception ex)
        {
            SelfLog.Write("FileSink: retention scan failed", ex);
        }
    }
}

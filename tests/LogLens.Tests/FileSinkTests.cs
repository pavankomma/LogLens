using LogLens.Configuration;
using LogLens.Formatting;
using LogLens.Sinks;
using Xunit;

namespace LogLens.Tests;

public sealed class FileSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LogLens.Tests", Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private FileSink Create(RollingInterval interval = RollingInterval.Infinite, long? size = null, int? retain = null)
        => new(Path.Combine(_dir, "app-.log"), new TextFormatter("{Message}"), interval, size, retain, () => _now);

    private static LogEvent Event(string msg) => new(DateTimeOffset.UnixEpoch, LogLevel.Information, msg);

    private string[] Files() => Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Infinite_interval_writes_single_file_and_appends()
    {
        using (var s = Create())
        {
            s.Emit(Event("one"));
            s.Flush();
            Assert.Equal(Path.Combine(_dir, "app-.log"), s.CurrentFilePath);
        }

        using (var s = Create()) s.Emit(Event("two"));
        Assert.Equal(["one", "two"], File.ReadAllLines(Path.Combine(_dir, "app-.log")));
    }

    [Fact]
    public void Rolls_by_day_when_clock_crosses_boundary()
    {
        using var s = Create(RollingInterval.Day);
        s.Emit(Event("d1"));
        _now = _now.AddHours(13); // same day
        s.Emit(Event("d1b"));
        _now = _now.AddHours(1);  // next day 00:00
        s.Emit(Event("d2"));
        s.Flush();
        Assert.Equal(["app-20260830.log", "app-20260831.log"], Files());
        Assert.Equal(["d1", "d1b"], File.ReadAllLines(Path.Combine(_dir, "app-20260830.log")));
    }

    [Fact]
    public void Rolls_by_size_with_sequence_suffix()
    {
        using var s = Create(RollingInterval.Day, size: 10);
        s.Emit(Event("aaaa")); // 4 + newline
        s.Emit(Event("bbbb")); // would exceed 10 → new file
        s.Emit(Event("cccc"));
        s.Flush();
        Assert.Equal(["app-20260830.log", "app-20260830_001.log", "app-20260830_002.log"], Files());
        Assert.EndsWith("_002.log", s.CurrentFilePath);
    }

    [Fact]
    public void Size_roll_skips_full_files_from_previous_run()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "app-20260830.log"), new string('x', 50));
        using var s = Create(RollingInterval.Day, size: 20);
        s.Emit(Event("new"));
        Assert.EndsWith("app-20260830_001.log", s.CurrentFilePath);
    }

    [Fact]
    public void Retention_deletes_oldest_files()
    {
        using var s = Create(RollingInterval.Day, retain: 2);
        for (var i = 0; i < 4; i++)
        {
            s.Emit(Event("x"));
            _now = _now.AddDays(1);
        }

        s.Flush();
        Assert.Equal(["app-20260901.log", "app-20260902.log"], Files());
    }

    [Theory]
    [InlineData(RollingInterval.Year, "2026")]
    [InlineData(RollingInterval.Month, "202608")]
    [InlineData(RollingInterval.Day, "20260830")]
    [InlineData(RollingInterval.Hour, "2026083010")]
    [InlineData(RollingInterval.Minute, "202608301000")]
    [InlineData(RollingInterval.Infinite, "")]
    public void PeriodToken_formats(RollingInterval interval, string expected)
        => Assert.Equal(expected, FileSink.PeriodToken(interval, _now));

    [Fact]
    public void NextCheckpoint_is_start_of_next_period()
    {
        var t = new DateTimeOffset(2026, 8, 30, 10, 15, 30, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), FileSink.NextCheckpoint(RollingInterval.Year, t));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), FileSink.NextCheckpoint(RollingInterval.Month, t));
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero), FileSink.NextCheckpoint(RollingInterval.Day, t));
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 11, 0, 0, TimeSpan.Zero), FileSink.NextCheckpoint(RollingInterval.Hour, t));
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 10, 16, 0, TimeSpan.Zero), FileSink.NextCheckpoint(RollingInterval.Minute, t));
        Assert.Equal(DateTimeOffset.MaxValue, FileSink.NextCheckpoint(RollingInterval.Infinite, t));
    }

    [Fact]
    public void Invalid_arguments_throw()
    {
        Assert.Throws<ArgumentException>(() => new FileSink(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSink("a.log", fileSizeLimitBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSink("a.log", retainedFileCount: 0));
    }
}

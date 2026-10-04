namespace LogLens.Configuration;

/// <summary>How often the file sink starts a new file.</summary>
public enum RollingInterval
{
    /// <summary>Never roll on time; a single file (size rolling may still apply).</summary>
    Infinite,
    /// <summary>New file each year: <c>app-2026.log</c>.</summary>
    Year,
    /// <summary>New file each month: <c>app-202608.log</c>.</summary>
    Month,
    /// <summary>New file each day: <c>app-20260830.log</c>.</summary>
    Day,
    /// <summary>New file each hour: <c>app-2026083014.log</c>.</summary>
    Hour,
    /// <summary>New file each minute: <c>app-202608301405.log</c>.</summary>
    Minute,
}

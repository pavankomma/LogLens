namespace LogLens;

/// <summary>Convenience methods on <see cref="ILogLensLogger"/>. Generic overloads avoid params-array allocation when a level is disabled.</summary>
public static class LoggerExtensions
{
    private static readonly object?[] NoArgs = [];

    // ---- Trace ----
    /// <summary>Writes a Trace event.</summary>
    public static void Trace(this ILogLensLogger logger, string messageTemplate) => Write0(logger, LogLevel.Trace, null, messageTemplate);
    /// <summary>Writes a Trace event.</summary>
    public static void Trace<T0>(this ILogLensLogger logger, string messageTemplate, T0 arg0) => Write1(logger, LogLevel.Trace, null, messageTemplate, arg0);
    /// <summary>Writes a Trace event.</summary>
    public static void Trace<T0, T1>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1) => Write2(logger, LogLevel.Trace, null, messageTemplate, arg0, arg1);
    /// <summary>Writes a Trace event.</summary>
    public static void Trace<T0, T1, T2>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1, T2 arg2) => Write3(logger, LogLevel.Trace, null, messageTemplate, arg0, arg1, arg2);
    /// <summary>Writes a Trace event.</summary>
    public static void Trace(this ILogLensLogger logger, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Trace, null, messageTemplate, args);
    /// <summary>Writes a Trace event with an exception.</summary>
    public static void Trace(this ILogLensLogger logger, Exception? exception, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Trace, exception, messageTemplate, args);

    // ---- Debug ----
    /// <summary>Writes a Debug event.</summary>
    public static void Debug(this ILogLensLogger logger, string messageTemplate) => Write0(logger, LogLevel.Debug, null, messageTemplate);
    /// <summary>Writes a Debug event.</summary>
    public static void Debug<T0>(this ILogLensLogger logger, string messageTemplate, T0 arg0) => Write1(logger, LogLevel.Debug, null, messageTemplate, arg0);
    /// <summary>Writes a Debug event.</summary>
    public static void Debug<T0, T1>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1) => Write2(logger, LogLevel.Debug, null, messageTemplate, arg0, arg1);
    /// <summary>Writes a Debug event.</summary>
    public static void Debug<T0, T1, T2>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1, T2 arg2) => Write3(logger, LogLevel.Debug, null, messageTemplate, arg0, arg1, arg2);
    /// <summary>Writes a Debug event.</summary>
    public static void Debug(this ILogLensLogger logger, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Debug, null, messageTemplate, args);
    /// <summary>Writes a Debug event with an exception.</summary>
    public static void Debug(this ILogLensLogger logger, Exception? exception, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Debug, exception, messageTemplate, args);

    // ---- Info ----
    /// <summary>Writes an Information event.</summary>
    public static void Info(this ILogLensLogger logger, string messageTemplate) => Write0(logger, LogLevel.Information, null, messageTemplate);
    /// <summary>Writes an Information event.</summary>
    public static void Info<T0>(this ILogLensLogger logger, string messageTemplate, T0 arg0) => Write1(logger, LogLevel.Information, null, messageTemplate, arg0);
    /// <summary>Writes an Information event.</summary>
    public static void Info<T0, T1>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1) => Write2(logger, LogLevel.Information, null, messageTemplate, arg0, arg1);
    /// <summary>Writes an Information event.</summary>
    public static void Info<T0, T1, T2>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1, T2 arg2) => Write3(logger, LogLevel.Information, null, messageTemplate, arg0, arg1, arg2);
    /// <summary>Writes an Information event.</summary>
    public static void Info(this ILogLensLogger logger, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Information, null, messageTemplate, args);
    /// <summary>Writes an Information event with an exception.</summary>
    public static void Info(this ILogLensLogger logger, Exception? exception, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Information, exception, messageTemplate, args);

    // ---- Warn ----
    /// <summary>Writes a Warning event.</summary>
    public static void Warn(this ILogLensLogger logger, string messageTemplate) => Write0(logger, LogLevel.Warning, null, messageTemplate);
    /// <summary>Writes a Warning event.</summary>
    public static void Warn<T0>(this ILogLensLogger logger, string messageTemplate, T0 arg0) => Write1(logger, LogLevel.Warning, null, messageTemplate, arg0);
    /// <summary>Writes a Warning event.</summary>
    public static void Warn<T0, T1>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1) => Write2(logger, LogLevel.Warning, null, messageTemplate, arg0, arg1);
    /// <summary>Writes a Warning event.</summary>
    public static void Warn<T0, T1, T2>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1, T2 arg2) => Write3(logger, LogLevel.Warning, null, messageTemplate, arg0, arg1, arg2);
    /// <summary>Writes a Warning event.</summary>
    public static void Warn(this ILogLensLogger logger, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Warning, null, messageTemplate, args);
    /// <summary>Writes a Warning event with an exception.</summary>
    public static void Warn(this ILogLensLogger logger, Exception? exception, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Warning, exception, messageTemplate, args);

    // ---- Error ----
    /// <summary>Writes an Error event.</summary>
    public static void Error(this ILogLensLogger logger, string messageTemplate) => Write0(logger, LogLevel.Error, null, messageTemplate);
    /// <summary>Writes an Error event.</summary>
    public static void Error<T0>(this ILogLensLogger logger, string messageTemplate, T0 arg0) => Write1(logger, LogLevel.Error, null, messageTemplate, arg0);
    /// <summary>Writes an Error event.</summary>
    public static void Error<T0, T1>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1) => Write2(logger, LogLevel.Error, null, messageTemplate, arg0, arg1);
    /// <summary>Writes an Error event.</summary>
    public static void Error<T0, T1, T2>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1, T2 arg2) => Write3(logger, LogLevel.Error, null, messageTemplate, arg0, arg1, arg2);
    /// <summary>Writes an Error event.</summary>
    public static void Error(this ILogLensLogger logger, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Error, null, messageTemplate, args);
    /// <summary>Writes an Error event with an exception.</summary>
    public static void Error(this ILogLensLogger logger, Exception? exception, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Error, exception, messageTemplate, args);

    // ---- Fatal ----
    /// <summary>Writes a Fatal event.</summary>
    public static void Fatal(this ILogLensLogger logger, string messageTemplate) => Write0(logger, LogLevel.Fatal, null, messageTemplate);
    /// <summary>Writes a Fatal event.</summary>
    public static void Fatal<T0>(this ILogLensLogger logger, string messageTemplate, T0 arg0) => Write1(logger, LogLevel.Fatal, null, messageTemplate, arg0);
    /// <summary>Writes a Fatal event.</summary>
    public static void Fatal<T0, T1>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1) => Write2(logger, LogLevel.Fatal, null, messageTemplate, arg0, arg1);
    /// <summary>Writes a Fatal event.</summary>
    public static void Fatal<T0, T1, T2>(this ILogLensLogger logger, string messageTemplate, T0 arg0, T1 arg1, T2 arg2) => Write3(logger, LogLevel.Fatal, null, messageTemplate, arg0, arg1, arg2);
    /// <summary>Writes a Fatal event.</summary>
    public static void Fatal(this ILogLensLogger logger, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Fatal, null, messageTemplate, args);
    /// <summary>Writes a Fatal event with an exception.</summary>
    public static void Fatal(this ILogLensLogger logger, Exception? exception, string messageTemplate, params object?[] args) => WriteN(logger, LogLevel.Fatal, exception, messageTemplate, args);

    private static void Write0(ILogLensLogger logger, LogLevel level, Exception? ex, string template)
    {
        if (logger is null || !logger.IsEnabled(level)) return;
        logger.Write(level, ex, template, NoArgs);
    }

    private static void Write1<T0>(ILogLensLogger logger, LogLevel level, Exception? ex, string template, T0 a0)
    {
        if (logger is null || !logger.IsEnabled(level)) return;
        logger.Write(level, ex, template, a0);
    }

    private static void Write2<T0, T1>(ILogLensLogger logger, LogLevel level, Exception? ex, string template, T0 a0, T1 a1)
    {
        if (logger is null || !logger.IsEnabled(level)) return;
        logger.Write(level, ex, template, a0, a1);
    }

    private static void Write3<T0, T1, T2>(ILogLensLogger logger, LogLevel level, Exception? ex, string template, T0 a0, T1 a1, T2 a2)
    {
        if (logger is null || !logger.IsEnabled(level)) return;
        logger.Write(level, ex, template, a0, a1, a2);
    }

    private static void WriteN(ILogLensLogger logger, LogLevel level, Exception? ex, string template, object?[] args)
    {
        if (logger is null || !logger.IsEnabled(level)) return;
        logger.Write(level, ex, template, args ?? NoArgs);
    }
}

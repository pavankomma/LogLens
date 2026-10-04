namespace LogLens;

/// <summary>Severity of a log event, lowest to highest.</summary>
public enum LogLevel
{
    /// <summary>Very detailed diagnostics.</summary>
    Trace = 0,
    /// <summary>Internal system events useful while developing.</summary>
    Debug = 1,
    /// <summary>Normal application behaviour.</summary>
    Information = 2,
    /// <summary>Something unexpected but recoverable.</summary>
    Warning = 3,
    /// <summary>A failure in the current operation.</summary>
    Error = 4,
    /// <summary>A failure that stops the application.</summary>
    Fatal = 5,
    /// <summary>Disables logging entirely.</summary>
    None = 6,
}

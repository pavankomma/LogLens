namespace LogLens.Configuration;

/// <summary>Behaviour of the async queue when full.</summary>
public enum OverflowPolicy
{
    /// <summary>Discard the oldest queued event to make room (default).</summary>
    DropOldest,
    /// <summary>Discard the event being written.</summary>
    DropNewest,
    /// <summary>Block the caller until space is available.</summary>
    Block,
}

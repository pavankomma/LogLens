namespace LogLens;

/// <summary>Async-local stack of scope properties shared by all loggers in the process.</summary>
public static class ScopeContext
{
    private static readonly AsyncLocal<Node?> Current = new();

    /// <summary>Pushes a property; dispose the result to pop it.</summary>
    public static IDisposable Push(string name, object? value)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Scope name is required.", nameof(name));
        var node = new Node(name, value, Current.Value);
        Current.Value = node;
        return node;
    }

    /// <summary>True if any scope is active on the current async flow.</summary>
    public static bool IsEmpty => Current.Value is null;

    /// <summary>Copies active scope properties into <paramref name="target"/>; innermost scopes win.</summary>
    public static void CopyTo(IDictionary<string, object?> target)
    {
        var node = Current.Value;
        if (node is null) return;

        // Walk outermost-first so inner values overwrite outer ones.
        var stack = new List<Node>();
        for (; node is not null; node = node.Parent) stack.Add(node);
        for (var i = stack.Count - 1; i >= 0; i--) target[stack[i].Name] = stack[i].Value;
    }

    private sealed class Node(string name, object? value, Node? parent) : IDisposable
    {
        private bool _disposed;

        public string Name { get; } = name;
        public object? Value { get; } = value;
        public Node? Parent { get; } = parent;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Only pop if we are still the innermost scope; a mis-ordered dispose is deferred until the
            // scopes above it are popped (we then skip over already-disposed ancestors).
            if (!ReferenceEquals(Current.Value, this)) return;
            var next = Parent;
            while (next is { _disposed: true }) next = next.Parent;
            Current.Value = next;
        }
    }
}

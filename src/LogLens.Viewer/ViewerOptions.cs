namespace LogLens.Viewer;

/// <summary>Command-line / configuration options for the viewer.</summary>
public sealed class ViewerOptions
{
    /// <summary>Ring-buffer capacity.</summary>
    public int Capacity { get; set; } = 200_000;

    /// <summary>
    /// Extra buffer retaining Warning+ events evicted from the ring; 0 disables, null (default) means capacity / 8.
    /// </summary>
    public int? ProtectedCapacity { get; set; }

    /// <summary>Files or directories to tail.</summary>
    public List<string> Tail { get; } = [];

    /// <summary>Optional shared secret required in <c>X-Api-Key</c> for ingest and delete.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Generate synthetic events for exploring the UI.</summary>
    public bool Demo { get; set; }

    /// <summary>Path to a domain profile JSON file (see <see cref="ViewerProfile"/>).</summary>
    public string? Profile { get; set; }

    /// <summary>Parses <c>--capacity N --tail PATH [--tail PATH] --api-key KEY</c>. Unknown args are ignored.</summary>
    public static ViewerOptions Parse(string[] args, IConfiguration? configuration = null)
    {
        var o = new ViewerOptions();
        if (configuration is not null)
        {
            if (int.TryParse(configuration["Viewer:Capacity"], out var cap) && cap > 0) o.Capacity = cap;
            if (int.TryParse(configuration["Viewer:ProtectedCapacity"], out var pcap) && pcap >= 0) o.ProtectedCapacity = pcap;
            o.ApiKey = configuration["Viewer:ApiKey"];
            o.Profile = configuration["Viewer:Profile"];
            foreach (var t in configuration.GetSection("Viewer:Tail").GetChildren()) if (!string.IsNullOrWhiteSpace(t.Value)) o.Tail.Add(t.Value!);
        }

        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--capacity": if (int.TryParse(Next(), out var c) && c > 0) o.Capacity = c; break;
                case "--protected-capacity": if (int.TryParse(Next(), out var pc) && pc >= 0) o.ProtectedCapacity = pc; break;
                case "--tail": if (Next() is { } p) o.Tail.Add(p); break;
                case "--api-key": o.ApiKey = Next(); break;
                case "--demo": o.Demo = true; break;
                case "--profile": o.Profile = Next(); break;
            }
        }

        return o;
    }
}

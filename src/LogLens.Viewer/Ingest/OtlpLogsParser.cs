using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using LogLens.Viewer.Store;

namespace LogLens.Viewer.Ingest;

/// <summary>
/// Minimal decoder for OTLP/HTTP logs (<c>opentelemetry.proto.collector.logs.v1.ExportLogsServiceRequest</c>,
/// binary protobuf). Hand-rolled for the subset the viewer needs — no protobuf dependency. Unknown fields are skipped,
/// so newer OTLP versions still parse.
/// </summary>
public static class OtlpLogsParser
{
    /// <summary>Parses an ExportLogsServiceRequest body into stored events.</summary>
    public static List<StoredEvent> Parse(ReadOnlySpan<byte> body, string? source = "otlp")
    {
        var events = new List<StoredEvent>();
        var r = new Reader(body);
        while (r.TryReadTag(out var field, out var wire))
        {
            if (field == 1 && wire == 2) ParseResourceLogs(r.ReadBytes(), events, source);   // resource_logs
            else r.Skip(wire);
        }

        return events;
    }

    private static void ParseResourceLogs(ReadOnlySpan<byte> data, List<StoredEvent> events, string? source)
    {
        var resourceAttrs = new Dictionary<string, string>(StringComparer.Ordinal);
        var scopes = new List<(string? Name, byte[] Bytes)>();
        var r = new Reader(data);
        while (r.TryReadTag(out var field, out var wire))
        {
            if (field == 1 && wire == 2)                                                      // Resource → attributes
            {
                var rr = new Reader(r.ReadBytes());
                while (rr.TryReadTag(out var f2, out var w2))
                {
                    if (f2 == 1 && w2 == 2) ParseKeyValues(rr.ReadBytes(), resourceAttrs);
                    else rr.Skip(w2);
                }
            }
            else if (field == 2 && wire == 2) scopes.Add((null, r.ReadBytes().ToArray()));    // scope_logs
            else r.Skip(wire);
        }

        foreach (var (_, scopeBytes) in scopes) ParseScopeLogs(scopeBytes, resourceAttrs, events, source);
    }

    private static void ParseKeyValues(ReadOnlySpan<byte> data, Dictionary<string, string> target)
    {
        var r = new Reader(data);
        string? key = null; string? value = null;
        while (r.TryReadTag(out var field, out var wire))
        {
            if (field == 1 && wire == 2) key = Encoding.UTF8.GetString(r.ReadBytes());
            else if (field == 2 && wire == 2) value = ReadAnyValue(r.ReadBytes());
            else r.Skip(wire);
        }

        if (key is { Length: > 0 }) target[key] = value ?? "";
    }

    private static string? ReadAnyValue(ReadOnlySpan<byte> data)
    {
        var r = new Reader(data);
        while (r.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == 2: return Encoding.UTF8.GetString(r.ReadBytes());                 // string_value
                case 2 when wire == 0: return r.ReadVarint() != 0 ? "true" : "false";                 // bool_value
                case 3 when wire == 0: return unchecked((long)r.ReadVarint()).ToString(CultureInfo.InvariantCulture); // int_value
                case 4 when wire == 1: return r.ReadDouble().ToString(CultureInfo.InvariantCulture);  // double_value
                case 5 when wire == 2:                                                                // array_value
                    {
                        var items = new List<string>();
                        var ar = new Reader(r.ReadBytes());
                        while (ar.TryReadTag(out var f2, out var w2))
                        {
                            if (f2 == 1 && w2 == 2) items.Add(ReadAnyValue(ar.ReadBytes()) ?? "");
                            else ar.Skip(w2);
                        }

                        return "[" + string.Join(", ", items) + "]";
                    }

                case 6 when wire == 2:                                                                // kvlist_value
                    {
                        var kvs = new Dictionary<string, string>(StringComparer.Ordinal);
                        var kr = new Reader(r.ReadBytes());
                        while (kr.TryReadTag(out var f2, out var w2))
                        {
                            if (f2 == 1 && w2 == 2) ParseKeyValues(kr.ReadBytes(), kvs);
                            else kr.Skip(w2);
                        }

                        return "{" + string.Join(", ", kvs.Select(kv => kv.Key + "=" + kv.Value)) + "}";
                    }

                case 7 when wire == 2: return Convert.ToHexString(r.ReadBytes()).ToLowerInvariant();   // bytes_value
                default: r.Skip(wire); break;
            }
        }

        return null;
    }

    private static void ParseScopeLogs(ReadOnlySpan<byte> data, Dictionary<string, string> resourceAttrs, List<StoredEvent> events, string? source)
    {
        string? scopeName = null;
        var records = new List<byte[]>();
        var r = new Reader(data);
        while (r.TryReadTag(out var field, out var wire))
        {
            if (field == 1 && wire == 2)                       // InstrumentationScope
            {
                var sr = new Reader(r.ReadBytes());
                while (sr.TryReadTag(out var f2, out var w2))
                {
                    if (f2 == 1 && w2 == 2) scopeName = Encoding.UTF8.GetString(sr.ReadBytes());
                    else sr.Skip(w2);
                }
            }
            else if (field == 2 && wire == 2) records.Add(r.ReadBytes().ToArray());   // log_records
            else r.Skip(wire);
        }

        foreach (var rec in records)
        {
            var e = ParseLogRecord(rec, resourceAttrs, scopeName, source);
            if (e is not null) events.Add(e);
        }
    }

    private static StoredEvent? ParseLogRecord(ReadOnlySpan<byte> data, Dictionary<string, string> resourceAttrs, string? scopeName, string? source)
    {
        ulong timeNano = 0, observedNano = 0;
        var severityNumber = 0;
        string? severityText = null, body = null, traceId = null, spanId = null;
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        var r = new Reader(data);
        while (r.TryReadTag(out var field, out var wire))
        {
            switch (field)
            {
                case 1 when wire == 1: timeNano = r.ReadFixed64(); break;
                case 11 when wire == 1: observedNano = r.ReadFixed64(); break;
                case 2 when wire == 0: severityNumber = (int)r.ReadVarint(); break;
                case 3 when wire == 2: severityText = Encoding.UTF8.GetString(r.ReadBytes()); break;
                case 5 when wire == 2: body = ReadAnyValue(r.ReadBytes()); break;
                case 6 when wire == 2: ParseKeyValues(r.ReadBytes(), attrs); break;
                case 9 when wire == 2: traceId = Convert.ToHexString(r.ReadBytes()).ToLowerInvariant(); break;
                case 10 when wire == 2: spanId = Convert.ToHexString(r.ReadBytes()).ToLowerInvariant(); break;
                default: r.Skip(wire); break;
            }
        }

        var nano = timeNano != 0 ? timeNano : observedNano;
        var timestamp = nano != 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(nano / 1_000_000)) : DateTimeOffset.UtcNow;

        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in resourceAttrs) props[kv.Key == "service.name" ? "App" : kv.Key] = kv.Value;
        foreach (var kv in attrs) props[kv.Key] = kv.Value;
        if (traceId is { Length: > 0 } && traceId.Trim('0').Length > 0) props["TraceId"] = traceId;
        if (spanId is { Length: > 0 } && spanId.Trim('0').Length > 0) props["SpanId"] = spanId;

        var message = body ?? "";
        if (message.Length == 0 && props.Count == 0) return null;
        return new StoredEvent
        {
            Timestamp = timestamp,
            Level = MapSeverity(severityNumber, severityText),
            Message = message,
            Category = scopeName is { Length: > 0 } ? scopeName : null,
            Exception = attrs.TryGetValue("exception.stacktrace", out var st) ? (attrs.TryGetValue("exception.type", out var et) ? et + ": " : "") + (attrs.TryGetValue("exception.message", out var em) ? em + Environment.NewLine : "") + st : null,
            Properties = props,
            Source = source,
        };
    }

    /// <summary>Maps OTLP severity (1–24) / text to a level.</summary>
    public static LogLevel MapSeverity(int number, string? text)
    {
        if (number > 0)
        {
            return number switch
            {
                <= 4 => LogLevel.Trace,
                <= 8 => LogLevel.Debug,
                <= 12 => LogLevel.Information,
                <= 16 => LogLevel.Warning,
                <= 20 => LogLevel.Error,
                _ => LogLevel.Fatal,
            };
        }

        return EventParser.ParseLevel(text);
    }

    /// <summary>Forward-only protobuf reader over a span.</summary>
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> _data = data;

        public bool TryReadTag(out int field, out int wire)
        {
            field = 0; wire = 0;
            if (_data.IsEmpty) return false;
            var tag = ReadVarint();
            field = (int)(tag >> 3);
            wire = (int)(tag & 0x7);
            return field != 0;
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            var shift = 0;
            while (!_data.IsEmpty)
            {
                var b = _data[0];
                _data = _data[1..];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift >= 64) break;
            }

            return result;
        }

        public ulong ReadFixed64()
        {
            if (_data.Length < 8) { _data = default; return 0; }
            var v = BinaryPrimitives.ReadUInt64LittleEndian(_data);
            _data = _data[8..];
            return v;
        }

        public double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadFixed64());

        public ReadOnlySpan<byte> ReadBytes()
        {
            var len = (int)ReadVarint();
            if (len < 0 || len > _data.Length) { _data = default; return default; }
            var v = _data[..len];
            _data = _data[len..];
            return v;
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0: ReadVarint(); break;
                case 1: ReadFixed64(); break;
                case 2: ReadBytes(); break;
                case 5: if (_data.Length >= 4) _data = _data[4..]; else _data = default; break;
                default: _data = default; break;   // unknown wire type: stop parsing this scope
            }
        }
    }
}

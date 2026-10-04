using System.Buffers.Binary;
using System.Net;
using System.Text;
using LogLens.Viewer.Ingest;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogLens.Viewer.Tests;

public class OtlpLogsParserTests
{
    // --- tiny protobuf writer for building test payloads ---
    private sealed class Pb
    {
        private readonly MemoryStream _ms = new();
        public byte[] Bytes => _ms.ToArray();
        public Pb Varint(int field, ulong value) { Tag(field, 0); WriteVarint(value); return this; }
        public Pb Fixed64(int field, ulong value) { Tag(field, 1); Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(b, value); _ms.Write(b); return this; }
        public Pb Len(int field, byte[] data) { Tag(field, 2); WriteVarint((ulong)data.Length); _ms.Write(data); return this; }
        public Pb Str(int field, string s) => Len(field, Encoding.UTF8.GetBytes(s));
        private void Tag(int field, int wire) => WriteVarint((ulong)(field << 3 | wire));
        private void WriteVarint(ulong v) { while (v >= 0x80) { _ms.WriteByte((byte)(v | 0x80)); v >>= 7; } _ms.WriteByte((byte)v); }
    }

    private static byte[] AnyString(string s) => new Pb().Str(1, s).Bytes;
    private static byte[] KeyValue(string k, byte[] anyValue) => new Pb().Str(1, k).Len(2, anyValue).Bytes;

    private static byte[] BuildRequest()
    {
        var resource = new Pb().Len(1, KeyValue("service.name", AnyString("billing"))).Len(1, KeyValue("deployment.environment", AnyString("prod"))).Bytes;
        var scope = new Pb().Str(1, "Billing.Api").Bytes;
        var nano = (ulong)DateTimeOffset.Parse("2026-08-30T10:00:00Z").ToUnixTimeMilliseconds() * 1_000_000UL;
        var record = new Pb()
            .Fixed64(1, nano)
            .Varint(2, 17)                                        // SEVERITY_NUMBER_ERROR
            .Str(3, "Error")
            .Len(5, AnyString("Invoice failed"))
            .Len(6, KeyValue("InvoiceId", AnyString("inv_1")))
            .Len(6, KeyValue("Attempts", new Pb().Varint(3, 3).Bytes))            // int_value
            .Len(6, KeyValue("exception.type", AnyString("System.TimeoutException")))
            .Len(6, KeyValue("exception.message", AnyString("timed out")))
            .Len(6, KeyValue("exception.stacktrace", AnyString("   at X.Y()")))
            .Len(9, Convert.FromHexString("0af7651916cd43dd8448eb211c80319c"))    // trace_id
            .Len(10, Convert.FromHexString("b7ad6b7169203331"))                   // span_id
            .Bytes;
        var scopeLogs = new Pb().Len(1, scope).Len(2, record).Bytes;
        var resourceLogs = new Pb().Len(1, resource).Len(2, scopeLogs).Bytes;
        return new Pb().Len(1, resourceLogs).Bytes;
    }

    [Fact]
    public static void Parses_export_request_with_resource_scope_severity_attrs_and_ids()
    {
        var events = OtlpLogsParser.Parse(BuildRequest());
        var e = Assert.Single(events);
        Assert.Equal("Invoice failed", e.Message);
        Assert.Equal(LogLevel.Error, e.Level);
        Assert.Equal("Billing.Api", e.Category);
        Assert.Equal(DateTimeOffset.Parse("2026-08-30T10:00:00Z"), e.Timestamp);
        Assert.Equal("billing", e.Properties["App"]);
        Assert.Equal("prod", e.Properties["deployment.environment"]);
        Assert.Equal("inv_1", e.Properties["InvoiceId"]);
        Assert.Equal("3", e.Properties["Attempts"]);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", e.Properties["TraceId"]);
        Assert.Equal("b7ad6b7169203331", e.Properties["SpanId"]);
        Assert.Contains("System.TimeoutException: timed out", e.Exception);
        Assert.Contains("at X.Y()", e.Exception);
        Assert.Equal("otlp", e.Source);
    }

    [Theory]
    [InlineData(1, LogLevel.Trace)]
    [InlineData(5, LogLevel.Debug)]
    [InlineData(9, LogLevel.Information)]
    [InlineData(13, LogLevel.Warning)]
    [InlineData(20, LogLevel.Error)]
    [InlineData(21, LogLevel.Fatal)]
    public static void Severity_number_maps(int n, LogLevel expected) => Assert.Equal(expected, OtlpLogsParser.MapSeverity(n, null));

    [Fact]
    public static void Garbage_and_empty_input_yield_no_events()
    {
        Assert.Empty(OtlpLogsParser.Parse([]));
        Assert.Empty(OtlpLogsParser.Parse(new byte[] { 0xFF, 0xFF, 0xFF }));
        Assert.Empty(OtlpLogsParser.Parse(Encoding.UTF8.GetBytes("not protobuf at all")));
    }

    [Fact]
    public static void Severity_text_fallback_used_when_number_missing() => Assert.Equal(LogLevel.Warning, OtlpLogsParser.MapSeverity(0, "warn"));

    [Fact]
    public async Task Otlp_http_endpoint_ingests_and_responds_with_protobuf()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(new ViewerOptions())));
        var client = factory.CreateClient();
        var content = new ByteArrayContent(BuildRequest());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        var resp = await client.PostAsync("/v1/logs", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/x-protobuf", resp.Content.Headers.ContentType!.MediaType);

        var events = await client.GetStringAsync("/api/events?q=InvoiceId=inv_1");
        Assert.Contains("Invoice failed", events);
        Assert.Contains("\"total\":1", events);
    }

    [Fact]
    public async Task Otlp_endpoint_honours_api_key()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(new ViewerOptions { ApiKey = "k" })));
        var client = factory.CreateClient();
        var body = () => { var c = new ByteArrayContent(BuildRequest()); c.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf"); return c; };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/v1/logs", body())).StatusCode);
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/logs") { Content = body() };
        req.Headers.Add("X-Api-Key", "k");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(req)).StatusCode);
    }
}

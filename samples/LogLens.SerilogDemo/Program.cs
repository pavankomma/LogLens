// A plain Serilog application — no LogLens packages at all — sending to LogLens.Viewer through the stock
// Serilog.Sinks.Seq, because the viewer speaks Seq's CLEF ingestion protocol (POST /ingest/clef).
//   dotnet run --project src/LogLens.Viewer
//   dotnet run --project samples/LogLens.SerilogDemo
using System.Diagnostics;
using Serilog;
using Serilog.Context;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("App", "serilog-demo")
    .WriteTo.Console()
    .WriteTo.Seq("http://localhost:5341")          // <-- LogLens.Viewer, unchanged Serilog sink
    .CreateLogger();

var rng = new Random(3);
var api = Log.ForContext("SourceContext", "Billing.Api");
var worker = Log.ForContext("SourceContext", "Billing.Worker");

Log.Information("Serilog demo starting; sending to {ServerUrl}", "http://localhost:5341");

for (var i = 0; i < 10; i++)
{
    var invoiceId = "inv_" + rng.Next(10_000, 99_999);
    using var activity = new Activity("ProcessInvoice").Start();         // Serilog ≥ 3.1 attaches @tr/@sp automatically
    using (LogContext.PushProperty("InvoiceId", invoiceId))
    using (LogContext.PushProperty("Customer", new[] { "acme", "globex", "initech" }[rng.Next(3)]))
    {
        api.Information("Invoice {InvoiceId} received with {LineItems} line items", invoiceId, rng.Next(1, 12));
        var sw = Stopwatch.StartNew();
        await Task.Delay(rng.Next(50, 700));
        if (rng.Next(5) == 0)
        {
            worker.Error(new InvalidOperationException("Tax service returned 503"), "Invoice {InvoiceId} processing failed after {Elapsed} ms", invoiceId, sw.ElapsedMilliseconds);
            continue;
        }

        worker.Debug("Tax computed for {InvoiceId}: {Tax:0.00}", invoiceId, rng.NextDouble() * 200);
        worker.Information("Invoice {InvoiceId} processed in {Elapsed} ms", invoiceId, sw.ElapsedMilliseconds);
    }
}

Log.Information("Serilog demo finished");
await Log.CloseAndFlushAsync();

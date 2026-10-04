using LogLens;
using LogLens.Configuration;
using LogLens.Formatting;
using LogLens.Sinks.Http;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel(LogLevel.Debug)
    .Enrich("App", "LogLens.Sample")
    .WriteTo.Console(ConsoleTheme.Ansi)
    .WriteTo.File("logs/app-.log", RollingInterval.Day, retainedFileCount: 7)
    .WriteTo.File("logs/app-.json", new JsonFormatter(), RollingInterval.Day, retainedFileCount: 7)
    .WriteTo.Http("http://localhost:5341")   // LogLens.Viewer, if running
    .Async()
    .CreateLogger();

Log.Info("Sample starting on {Machine} at {Time:HH:mm}", Environment.MachineName, DateTime.Now);

using (Log.BeginScope("RequestId", Guid.NewGuid()))
{
    var orders = Log.ForContext("Component", "Orders");
    orders.Debug("Loading {Count} orders", 3);
    orders.Info("Order {OrderId} total {Total:C}", 1001, 42.5m);
    orders.Warn("Order {OrderId} shipped late by {Days,3} days", 1002, 2);
}

try
{
    throw new InvalidOperationException("Payment gateway timeout");
}
catch (Exception ex)
{
    Log.Error(ex, "Payment failed for {OrderId}", 1003);
}

Log.Info("Braces render literally: {{not a hole}} and unbound {Missing}");
Log.Info("Done. See logs/ directory.");
Log.CloseAndFlush();

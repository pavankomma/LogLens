using System.Globalization;
using LogLens.Viewer.Store;

namespace LogLens.Viewer.Ingest;

/// <summary>Generates realistic-looking synthetic events (<c>--demo</c>) so the UI can be explored without a producer.</summary>
public sealed class DemoSource(LogStore store, int perSecond = 5) : BackgroundService
{
    private static readonly string[] Categories = ["Shop.Orders", "Shop.Payments", "Shop.Inventory", "Auth", "Http.Server"];
    private static readonly string[] Users = ["ann", "bob", "carol", "dave", "erin"];
    private static readonly string[] Routes = ["/api/orders", "/api/cart", "/api/checkout", "/health", "/api/products"];

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rng = new Random(42);
        var delay = TimeSpan.FromMilliseconds(Math.Max(20, 1000 / Math.Max(1, perSecond)));
        var orderId = 1000;
        while (!stoppingToken.IsCancellationRequested)
        {
            store.Add([Next(rng, ref orderId)]);
            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static StoredEvent Next(Random rng, ref int orderId)
    {
        var roll = rng.Next(100);
        var user = Users[rng.Next(Users.Length)];
        var elapsed = Math.Round(rng.NextDouble() * (roll > 92 ? 3000 : 400), 1);
        var props = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RequestId"] = Guid.NewGuid().ToString("N")[..12],
            ["User"] = user,
            ["Elapsed"] = elapsed.ToString(CultureInfo.InvariantCulture),
            ["App"] = roll < 55 ? "gateway" : roll < 75 ? "orders" : roll < 85 ? "auth" : roll < 93 ? "inventory" : "payments",
            ["TraceId"] = Guid.NewGuid().ToString("N"),
        };

        LogLevel level; string message; string? exception = null; string category;
        if (roll < 55)
        {
            var route = Routes[rng.Next(Routes.Length)];
            var status = roll < 50 ? 200 : 404;
            props["Route"] = route; props["Status"] = status.ToString(CultureInfo.InvariantCulture);
            category = "Http.Server"; level = status == 200 ? LogLevel.Information : LogLevel.Warning;
            message = $"{(rng.Next(3) == 0 ? "POST" : "GET")} {route} responded {status} in {elapsed} ms";
        }
        else if (roll < 75)
        {
            orderId++;
            props["OrderId"] = orderId.ToString(CultureInfo.InvariantCulture);
            props["Total"] = (rng.Next(500, 25000) / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
            category = "Shop.Orders"; level = LogLevel.Information;
            message = $"Order {orderId} placed by {user} for {props["Total"]}";
        }
        else if (roll < 85)
        {
            category = "Auth"; level = rng.Next(4) == 0 ? LogLevel.Warning : LogLevel.Debug;
            message = level == LogLevel.Warning ? $"Login failed for {user}: bad password" : $"Token refreshed for {user}";
        }
        else if (roll < 93)
        {
            category = "Shop.Inventory"; level = LogLevel.Trace;
            message = $"Stock check for sku-{rng.Next(100, 999)}: {rng.Next(0, 50)} left";
        }
        else if (roll < 98)
        {
            props["OrderId"] = orderId.ToString(CultureInfo.InvariantCulture);
            category = "Shop.Payments"; level = LogLevel.Error;
            message = $"Payment failed for order {orderId}";
            exception = "System.Net.Http.HttpRequestException: Gateway timeout after " + elapsed + " ms\n   at Shop.Payments.Gateway.ChargeAsync(Order order)\n   at Shop.Payments.PaymentService.ProcessAsync(Int32 orderId)";
        }
        else
        {
            category = Categories[rng.Next(Categories.Length)]; level = LogLevel.Fatal;
            message = "Unhandled exception; process is terminating";
            exception = "System.OutOfMemoryException: Insufficient memory to continue the execution of the program.";
        }

        return new StoredEvent
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Message = message,
            Category = category,
            Exception = exception,
            Properties = props,
            Source = "demo",
        };
    }
}

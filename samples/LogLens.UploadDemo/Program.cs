// Demonstrates how one document upload produces a *chain* of correlated log events across three services
// (upload-api → scanner → ocr) using Log.BeginScope("DocumentId", ...). Run the viewer first:
//   dotnet run --project src/LogLens.Viewer
// then:
//   dotnet run --project samples/LogLens.UploadDemo -- [count]
using System.Diagnostics;
using System.Security.Cryptography;
using LogLens;
using LogLens.Configuration;
using LogLens.Sinks.Http;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel(LogLevel.Debug)
    .Enrich("Env", "demo")
    .WriteTo.Console(ConsoleTheme.Ansi, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {App,-10} {Message}{Exception}")
    .WriteTo.Http("http://localhost:5341", batchSize: 20, period: TimeSpan.FromMilliseconds(300))
    .Async()
    .CreateLogger();

var count = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 12;
var rng = new Random(7);
string[] names = ["invoice-2026-08.pdf", "contract-acme.docx", "passport-scan.jpg", "lease-agreement.pdf", "receipt.png", "w9-form.pdf"];
string[] users = ["ann", "bob", "carol", "dave"];

var uploadApi = Log.ForContext("App", "upload-api");
var scanner = Log.ForContext("App", "scanner");
var ocr = Log.ForContext("App", "ocr");

uploadApi.Info("Upload demo starting: {Count} documents", count);

for (var i = 0; i < count; i++)
{
    var docId = "doc_" + Guid.NewGuid().ToString("N")[..10];
    var fileName = names[rng.Next(names.Length)];
    var bytes = rng.Next(40_000, 12_000_000);
    var contentType = fileName.EndsWith(".pdf") ? "application/pdf" : fileName.EndsWith(".docx") ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : "image/" + fileName[(fileName.LastIndexOf('.') + 1)..];
    var userId = users[rng.Next(users.Length)];
    var tenant = "tenant-" + (char)('A' + rng.Next(3));

    // ---- everything inside this scope carries DocumentId (and TenantId) — that's the correlation key ----
    using (Log.BeginScope("DocumentId", docId))
    using (Log.BeginScope("TenantId", tenant))
    {
        uploadApi.Info("Upload received {FileName} {Bytes} {ContentType} from {UserId}", fileName, bytes, contentType, userId);

        var sw = Stopwatch.StartNew();
        await Task.Delay(rng.Next(80, 400));                               // storage.SaveAsync(file)
        var key = $"{tenant}/{DateTime.UtcNow:yyyy/MM}/{docId}/{fileName}";
        var hash = Convert.ToHexString(SHA256.HashData(BitConverter.GetBytes(bytes)))[..16].ToLowerInvariant();
        uploadApi.Info("Upload stored {StorageKey} {Sha256} in {Elapsed} ms", key, hash, sw.ElapsedMilliseconds);

        sw.Restart();
        await Task.Delay(rng.Next(50, 250));                               // antivirus
        var infected = rng.Next(12) == 0;
        var result = infected ? "infected" : "clean";
        if (infected)
        {
            scanner.Warn("Virus scan {ScanResult} for {StorageKey}: {Signature}", result, key, "EICAR-Test-File");
            uploadApi.Error(new InvalidOperationException($"Document {docId} quarantined after failed virus scan"), "Upload rejected {FileName} for {UserId}", fileName, userId);
            continue;
        }

        scanner.Info("Virus scan {ScanResult} for {StorageKey} in {Elapsed} ms", result, key, sw.ElapsedMilliseconds);

        sw.Restart();
        var pages = fileName.EndsWith(".pdf") || fileName.EndsWith(".docx") ? rng.Next(1, 40) : 1;
        await Task.Delay(rng.Next(120, 900));                              // OCR
        if (rng.Next(9) == 0)
        {
            ocr.Error(new TimeoutException("OCR engine did not respond within 30 s"), "OCR failed on page {Page} of {Pages}", rng.Next(1, pages + 1), pages);
            ocr.Warn("Document queued for retry {RetryIn}", TimeSpan.FromMinutes(5));
            continue;
        }

        var ocrMs = sw.ElapsedMilliseconds;
        ocr.Debug("OCR language detected {Language} with {Confidence:0.00}", "en", 0.8 + rng.NextDouble() * 0.2);
        ocr.Info("OCR finished {Pages} pages in {Elapsed} ms", pages, ocrMs);
        uploadApi.Info("Document ready {FileName} ({Pages} pages, {Bytes} bytes) for {UserId}", fileName, pages, bytes, userId);
    }

    await Task.Delay(rng.Next(200, 600));
}

uploadApi.Info("Upload demo finished");
Log.CloseAndFlush();

using LogLens.Viewer;
using LogLens.Viewer.Api;
using LogLens.Viewer.Ingest;
using LogLens.Viewer.Store;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://localhost:5341");

var options = ViewerOptions.Parse(args, builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddOpenApi();
builder.Services.AddSingleton(new LogStore(options.Capacity, options.ProtectedCapacity));
builder.Services.AddSingleton(ViewerProfile.Load(options.Profile));
if (options.Tail.Count > 0)
    builder.Services.AddHostedService(sp => new FileTailSource(sp.GetRequiredService<LogStore>(), options.Tail));

if (options.Demo)
    builder.Services.AddHostedService(sp => new DemoSource(sp.GetRequiredService<LogStore>()));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});
app.MapViewerApi();

// OpenAPI document + Swagger UI, served by the same host as the SPA and the API.
app.MapOpenApi();                                     // /openapi/v1.json
app.UseSwaggerUI(o =>                                 // /swagger
{
    o.SwaggerEndpoint("/openapi/v1.json", "LogLens Viewer API v1");
    o.DocumentTitle = "LogLens Viewer API";
});

app.Logger.LogInformation("LogLens.Viewer capacity={Capacity} tail={Tail} apiKey={ApiKey} demo={Demo}",
    options.Capacity, string.Join(";", options.Tail), options.ApiKey is null ? "off" : "on", options.Demo);
app.Run();

/// <summary>Entry point marker for WebApplicationFactory.</summary>
public partial class Program;

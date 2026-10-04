using LogLens.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LogLens.Extensions.Logging;

/// <summary>Registration helpers for <see cref="ILoggingBuilder"/>.</summary>
public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Adds LogLens as an MEL provider. When <paramref name="logger"/> is null, output goes to <see cref="Log.Logger"/>.
    /// </summary>
    public static ILoggingBuilder AddLogLens(this ILoggingBuilder builder, ILogLensLogger? logger = null, bool dispose = false)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider>(new LogLensProvider(logger, dispose)));
        if (logger is not null) builder.Services.TryAddSingleton(logger);
        return builder;
    }

    /// <summary>
    /// Adds LogLens configured in code: <c>builder.Logging.AddLogLens(cfg => cfg.WriteTo.Http("http://localhost:5341"))</c>.
    /// The logger is owned by the provider and disposed with the host.
    /// </summary>
    public static ILoggingBuilder AddLogLens(this ILoggingBuilder builder, Action<LoggerConfiguration> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var cfg = new LoggerConfiguration();
        configure(cfg);
        var logger = cfg.CreateLogger();
        Log.Logger = logger;
        return builder.AddLogLens(logger, dispose: true);
    }

    /// <summary>
    /// Adds LogLens from a configuration section (conventionally <c>Logging:LogLens</c>); see <see cref="LogLensOptions"/>
    /// for the shape. Values can also come from environment variables (<c>Logging__LogLens__Viewer</c>).
    /// </summary>
    public static ILoggingBuilder AddLogLens(this ILoggingBuilder builder, IConfiguration section)
    {
        if (section is null) throw new ArgumentNullException(nameof(section));
        var options = new LogLensOptions();
        section.Bind(options);
        var logger = options.CreateLogger();
        Log.Logger = logger;
        return builder.AddLogLens(logger, dispose: true);
    }
}

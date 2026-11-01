using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BreweryCrawl.Api.Infrastructure.Logging;

public static class LoggingSetup
{
    private const string ReadableTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {SourceContext}{NewLine}      {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Logs to the console. Readable text by default; JSON when <c>Logging:Format</c> is "json" (e.g. in Azure).
    /// Every line inside a request carries the TraceId, which is also returned in ProblemDetails responses.
    /// </summary>
    public static WebApplicationBuilder AddLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext();

            if (string.Equals(builder.Configuration["Logging:Format"], "json", StringComparison.OrdinalIgnoreCase))
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
            else
            {
                logger.WriteTo.Console(outputTemplate: ReadableTemplate);
            }
        });

        return builder;
    }

    /// <summary>One summary line per request. Level depends on the outcome: 5xx = Error, 4xx = Warning.</summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.GetLevel = (context, _, exception) => context.Response.StatusCode switch
            {
                _ when exception is not null => LogEventLevel.Error,
                >= 500 => LogEventLevel.Error,
                >= 400 => LogEventLevel.Warning,
                _ => LogEventLevel.Information,
            };
        });
}

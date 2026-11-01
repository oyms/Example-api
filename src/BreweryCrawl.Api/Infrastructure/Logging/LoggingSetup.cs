using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace BreweryCrawl.Api.Infrastructure.Logging;

public static class LoggingSetup
{
    private const string ReadableTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {TraceId} {SourceContext}{NewLine}      {Message:lj} {ErrorCode}{NewLine}{Exception}";

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

    /// <summary>
    /// One summary line per request, including the errorCode of any error response and the user.
    /// Level depends on the outcome: 5xx = Error, 4xx = Warning.
    /// </summary>
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms (user: {User})";
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("User", context.User.Identity?.Name ?? "anonymous");

                // Only set for error responses. Shown at the end of the line by the console template, and as a
                // separate field in JSON logs, so you can search for e.g. ErrorCode = "crawl.not_owner".
                if (context.Items[ErrorHandlingSetup.ErrorCodeItemKey] is string errorCode)
                {
                    diagnostics.Set("ErrorCode", errorCode);
                }
            };

            options.GetLevel = (context, _, exception) => context.Response.StatusCode switch
            {
                _ when exception is not null => LogEventLevel.Error,
                >= 500 => LogEventLevel.Error,
                >= 400 => LogEventLevel.Warning,
                _ => LogEventLevel.Information,
            };
        });
}

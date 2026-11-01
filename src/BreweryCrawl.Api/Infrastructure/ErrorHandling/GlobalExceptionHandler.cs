using Microsoft.AspNetCore.Diagnostics;

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Last line of defence: turns every exception that escapes an endpoint into a ProblemDetails response.
/// Known exception types get a specific status and error code; everything else becomes a 500 that does
/// not leak internals (unless exception details are enabled, e.g. in Development).
/// Requests cancelled by the client never get here: ASP.NET Core responds 499 for those by itself.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IConfiguration configuration,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (definition, detail) = Classify(exception);

        switch (definition.Status)
        {
            case >= 500 when exception is not UpstreamException:
                logger.LogError(exception, "Unhandled exception");
                break;
            case >= 500:
                // Expected kind of failure with a clear cause: one line is more useful than a stack trace.
                logger.LogWarning("Upstream failure: {Reason}", exception.Message);
                break;
            default:
                logger.LogInformation("Bad request: {Detail}", detail);
                break;
        }

        var problem = ErrorResults.CreateProblemDetails(definition, detail);
        // Off by default, also in Development. Turn it on to see why it must stay off in production.
        if (configuration.GetValue<bool>("ErrorHandling:IncludeExceptionDetails"))
        {
            problem.Extensions["exception"] = new
            {
                type = exception.GetType().FullName,
                message = exception.Message,
                stackTrace = exception.StackTrace,
            };
        }

        httpContext.Response.StatusCode = definition.Status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
        return true;
    }

    private static (ErrorDefinition Definition, string Detail) Classify(Exception exception) => exception switch
    {
        // Thrown by minimal APIs when the request can't be bound, e.g. ?perPage=abc or invalid JSON.
        // Its StatusCode can also be 413 (body too large), 415 (wrong content type) etc.
        BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest } bad => (CommonErrors.MalformedRequest, bad.Message),
        BadHttpRequestException bad => (CommonErrors.ForStatus(bad.StatusCode), bad.Message),

        UpstreamException { Failure: UpstreamFailure.Timeout } up =>
            (CommonErrors.UpstreamTimeout, $"{up.Service} did not respond in time."),
        UpstreamException { Failure: UpstreamFailure.CircuitOpen } up =>
            (CommonErrors.UpstreamCircuitOpen, $"{up.Service} has failed repeatedly and is temporarily not being called."),
        UpstreamException up =>
            (CommonErrors.UpstreamUnavailable, $"{up.Service} returned an error."),

        // Never echo exception.Message here: it may contain internals (SQL, file paths, secrets).
        _ => (CommonErrors.Unexpected, "An unexpected error occurred. Please try again later."),
    };
}

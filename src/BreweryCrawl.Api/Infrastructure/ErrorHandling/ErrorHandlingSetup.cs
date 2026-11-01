using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

public static class ErrorHandlingSetup
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Registered before AddProblemDetails, so it is used instead of the built-in writer.
        services.AddSingleton<IProblemDetailsWriter, JsonProblemDetailsWriter>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Enrich);

        // Turn binding failures (e.g. ?page=abc) into BadHttpRequestException in all environments,
        // so GlobalExceptionHandler can return a proper ProblemDetails body with an explanation.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        return services;
    }

    /// <summary>
    /// Runs for every ProblemDetails response: our own errors, validation errors, exceptions
    /// and framework responses such as 404 for an unknown URL.
    /// </summary>
    private static void Enrich(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;

        // RFC 9457: a URI reference identifying this occurrence of the problem.
        problem.Instance ??= context.HttpContext.Request.Path;

        // Same id as the TraceId in the logs, so a user can report it and we can find the request.
        problem.Extensions["traceId"] = GetTraceId(context.HttpContext);

        // Make sure every problem has an errorCode and a matching type, also those created by the framework.
        if (!problem.Extensions.ContainsKey("errorCode"))
        {
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;
            var definition = status switch
            {
                StatusCodes.Status400BadRequest => CommonErrors.MalformedRequest,
                StatusCodes.Status404NotFound => CommonErrors.RouteNotFound,
                StatusCodes.Status405MethodNotAllowed => CommonErrors.MethodNotAllowed,
                StatusCodes.Status500InternalServerError => CommonErrors.Unexpected,
                _ => CommonErrors.ForStatus(status),
            };

            problem.Extensions["errorCode"] = definition.Code;
            problem.Type = definition.TypeUri;
            problem.Title = definition.Title;
            problem.Detail ??= definition.Description;
        }
    }

    public static string GetTraceId(HttpContext httpContext) =>
        Activity.Current?.TraceId.ToHexString()
        ?? httpContext.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToHexString()
        ?? httpContext.TraceIdentifier;
}

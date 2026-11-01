using Microsoft.Extensions.Options;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Writes ProblemDetails as JSON regardless of the Accept header. The built-in writer only answers clients
/// that accept JSON, so opening e.g. <c>/api/breweries?chaos=fail</c> in a browser (Accept: text/html)
/// would otherwise give an error with an empty body.
/// </summary>
public sealed class JsonProblemDetailsWriter(
    IOptions<ProblemDetailsOptions> problemDetailsOptions,
    IOptions<JsonOptions> jsonOptions) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Status ??= context.HttpContext.Response.StatusCode;
        problemDetailsOptions.Value.CustomizeProblemDetails?.Invoke(context);

        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            problem, problem.GetType(), jsonOptions.Value.SerializerOptions, "application/problem+json"));
    }
}

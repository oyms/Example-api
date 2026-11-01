using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

public static class ErrorResults
{
    /// <summary>
    /// Converts an expected <see cref="Error"/> to an RFC 9457 ProblemDetails response.
    /// traceId and instance are added for all problems in <see cref="ErrorHandlingSetup"/>.
    /// </summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        var problem = CreateProblemDetails(error.Definition, error.Detail);
        if (error.Parameters is { Count: > 0 })
        {
            problem.Extensions["params"] = error.Parameters;
        }

        return TypedResults.Problem(problem);
    }

    public static ProblemDetails CreateProblemDetails(ErrorDefinition definition, string detail) => new()
    {
        Type = definition.TypeUri,
        Title = definition.Title,
        Status = definition.Status,
        Detail = detail,
        Extensions = { ["errorCode"] = definition.Code },
    };
}

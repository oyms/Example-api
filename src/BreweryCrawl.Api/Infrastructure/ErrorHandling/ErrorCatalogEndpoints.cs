using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Infrastructure.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Documents every error the API can return. The ProblemDetails "type" of each error points here,
/// and clients can use the list to build translations for each errorCode.
/// </summary>
public static class ErrorCatalogEndpoints
{
    public static IReadOnlyList<ErrorDefinition> All { get; } = [.. CommonErrors.All, .. BreweryErrors.All];

    public sealed record ErrorCatalog(IReadOnlyList<ErrorDefinition> Errors, IReadOnlyList<string> ValidationCodes);

    public static IEndpointRouteBuilder MapErrorCatalog(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/errors").WithTags("Errors");

        group.MapGet("/", () => TypedResults.Ok(new ErrorCatalog(All, ValidationCodes.All)))
            .WithSummary("List all error codes");

        group.MapGet("/{code}", Results<Ok<ErrorDefinition>, ProblemHttpResult> (string code) =>
                Find(code) is { } definition
                    ? TypedResults.Ok(definition)
                    : CommonErrors.ErrorCodeNotFound.WithDetail($"There is no error with code '{code}'.").ToProblem())
            .WithSummary("Describe one error code");

        return app;
    }

    private static ErrorDefinition? Find(string code) =>
        All.FirstOrDefault(e => e.Code == code)
        ?? (code.StartsWith("http.") && int.TryParse(code["http.".Length..], out var status) && status is >= 400 and < 600
            ? CommonErrors.ForStatus(status)
            : null);
}

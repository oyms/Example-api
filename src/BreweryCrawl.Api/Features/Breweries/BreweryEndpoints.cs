using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using BreweryCrawl.Api.Infrastructure.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BreweryCrawl.Api.Features.Breweries;

public sealed record ListBreweriesQuery(string? City, string? Name, string? Type, int Page = 1, int PerPage = 20) : IValidatable
{
    public void Validate(ValidationErrors errors) => errors
        .MaxLength("city", City, 100)
        .MaxLength("name", Name, 100)
        .OneOf("type", Type, BreweryTypes.All)
        .Range("page", Page, 1, 500)
        .Range("perPage", PerPage, 1, 50);
}

public static class BreweryEndpoints
{
    public const string DataSourceHeader = "X-Data-Source";

    public static IEndpointRouteBuilder MapBreweryEndpoints(this IEndpointRouteBuilder app)
    {
        // Declared so the error contract shows up in the API docs.
        var group = app.MapGroup("/api/breweries")
            .WithTags("Breweries")
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        group.MapGet("/", ListBreweries)
            .WithValidation<ListBreweriesQuery>()
            .WithSummary("List breweries")
            .WithDescription("Data comes from Open Brewery DB, with a bundled snapshot as fallback. See the X-Data-Source response header.");

        group.MapGet("/{id}", GetBrewery)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Get a brewery by id");

        return app;
    }

    private static async Task<Ok<BreweryPage>> ListBreweries(
        [AsParameters] ListBreweriesQuery query,
        BreweryCatalog catalog,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var filter = new BreweryFilter(query.City, query.Name, query.Type?.ToLowerInvariant(), query.Page, query.PerPage);
        var (breweries, source) = await catalog.ListAsync(filter, cancellationToken);

        response.Headers[DataSourceHeader] = source.ToString().ToLowerInvariant();
        return TypedResults.Ok(new BreweryPage(breweries, query.Page, query.PerPage));
    }

    private static async Task<Results<Ok<Brewery>, ProblemHttpResult>> GetBrewery(
        string id,
        BreweryCatalog catalog,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await catalog.GetAsync(id, cancellationToken);

        return result.Match<Results<Ok<Brewery>, ProblemHttpResult>>(
            found =>
            {
                response.Headers[DataSourceHeader] = found.Source.ToString().ToLowerInvariant();
                return TypedResults.Ok(found.Value);
            },
            error => error.ToProblem());
    }
}

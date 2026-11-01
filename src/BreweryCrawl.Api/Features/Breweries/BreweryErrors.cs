using BreweryCrawl.Api.Infrastructure.ErrorHandling;

namespace BreweryCrawl.Api.Features.Breweries;

public static class BreweryErrors
{
    public static readonly ErrorDefinition NotFound = new(
        "brewery.not_found", StatusCodes.Status404NotFound, "Brewery not found",
        "No brewery exists with the given id.");

    public static Error NotFoundError(string id) => NotFound.WithDetail(
        $"No brewery exists with id '{id}'.",
        new Dictionary<string, object?> { ["id"] = id });

    public static IEnumerable<ErrorDefinition> All => [NotFound];
}

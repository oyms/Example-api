using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;

namespace BreweryCrawl.Api.Features.Crawls;

public static class CrawlErrors
{
    public static readonly ErrorDefinition NotFound = new(
        "crawl.not_found", StatusCodes.Status404NotFound, "Crawl not found",
        "No crawl exists with the given id.");

    public static readonly ErrorDefinition StopNotFound = new(
        "crawl.stop_not_found", StatusCodes.Status404NotFound, "Stop not found",
        "The crawl has no stop at the given brewery.");

    public static readonly ErrorDefinition NotOwner = new(
        "crawl.not_owner", StatusCodes.Status403Forbidden, "Not your crawl",
        "Only the user who created a crawl can change it.");

    public static readonly ErrorDefinition DuplicateStop = new(
        "crawl.duplicate_stop", StatusCodes.Status409Conflict, "Brewery already in crawl",
        "The brewery is already a stop on this crawl.");

    public static readonly ErrorDefinition VersionMismatch = new(
        "crawl.version_mismatch", StatusCodes.Status412PreconditionFailed, "Crawl has been changed",
        "The crawl was changed after you read it (If-Match does not match the current ETag). Fetch it again and retry.");

    public static readonly ErrorDefinition TooManyStops = new(
        "crawl.too_many_stops", StatusCodes.Status422UnprocessableEntity, "Too many stops",
        $"A crawl can have at most {Crawl.MaxStops} stops.");

    public static readonly ErrorDefinition WrongCity = new(
        "crawl.brewery_wrong_city", StatusCodes.Status422UnprocessableEntity, "Brewery is in another city",
        "All stops on a crawl must be in the crawl's city.");

    public static readonly ErrorDefinition BreweryNotOpen = new(
        "crawl.brewery_not_open", StatusCodes.Status422UnprocessableEntity, "Brewery is not open",
        "Closed breweries and breweries still being planned can't be added to a crawl.");

    public static readonly ErrorDefinition UnknownBrewery = new(
        "crawl.unknown_brewery", StatusCodes.Status422UnprocessableEntity, "Unknown brewery",
        "The request refers to a brewery that does not exist.");

    public static readonly ErrorDefinition InThePast = new(
        "crawl.in_the_past", StatusCodes.Status422UnprocessableEntity, "Crawl has already happened",
        "A crawl can't be changed after its date has passed.");

    public static IEnumerable<ErrorDefinition> All =>
    [
        NotFound, StopNotFound, NotOwner, DuplicateStop, VersionMismatch,
        TooManyStops, WrongCity, BreweryNotOpen, UnknownBrewery, InThePast,
    ];

    public static Error NotFoundError(Guid id) => NotFound.WithDetail(
        $"No crawl exists with id '{id}'.",
        new Dictionary<string, object?> { ["id"] = id });

    public static Error StopNotFoundError(string breweryId) => StopNotFound.WithDetail(
        $"The crawl has no stop at brewery '{breweryId}'.",
        new Dictionary<string, object?> { ["breweryId"] = breweryId });

    public static Error NotOwnerError(Crawl crawl) => NotOwner.WithDetail(
        $"The crawl '{crawl.Name}' belongs to another user.");

    public static Error DuplicateStopError(Brewery brewery) => DuplicateStop.WithDetail(
        $"'{brewery.Name}' is already a stop on this crawl.",
        new Dictionary<string, object?> { ["breweryId"] = brewery.Id });

    public static Error VersionMismatchError(int currentVersion) => VersionMismatch.WithDetail(
        "The crawl was changed after you read it. Fetch it again and retry.",
        new Dictionary<string, object?> { ["currentVersion"] = currentVersion });

    public static Error TooManyStopsError() => TooManyStops.WithDetail(
        $"A crawl can have at most {Crawl.MaxStops} stops.",
        new Dictionary<string, object?> { ["maxStops"] = Crawl.MaxStops });

    public static Error WrongCityError(Brewery brewery, Crawl crawl) => WrongCity.WithDetail(
        $"'{brewery.Name}' is in {brewery.City}, but this crawl is in {crawl.City}.",
        new Dictionary<string, object?> { ["breweryCity"] = brewery.City, ["crawlCity"] = crawl.City });

    public static Error BreweryNotOpenError(Brewery brewery) => BreweryNotOpen.WithDetail(
        $"'{brewery.Name}' can't be added because its type is '{brewery.Type}'.",
        new Dictionary<string, object?> { ["breweryType"] = brewery.Type });

    public static Error UnknownBreweryError(string breweryId) => UnknownBrewery.WithDetail(
        $"No brewery exists with id '{breweryId}'.",
        new Dictionary<string, object?> { ["breweryId"] = breweryId });

    public static Error InThePastError(Crawl crawl) => InThePast.WithDetail(
        $"The crawl '{crawl.Name}' took place on {crawl.Date:yyyy-MM-dd} and can no longer be changed.",
        new Dictionary<string, object?> { ["date"] = crawl.Date });
}

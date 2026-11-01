using BreweryCrawl.Api.Infrastructure.Validation;

namespace BreweryCrawl.Api.Features.Crawls;

public sealed record CrawlResponse(
    Guid Id,
    string Name,
    string City,
    DateOnly Date,
    string Owner,
    int Version,
    IReadOnlyList<CrawlStopResponse> Stops)
{
    public static CrawlResponse From(Crawl crawl) => new(
        crawl.Id, crawl.Name, crawl.City, crawl.Date, crawl.OwnerId, crawl.Version,
        crawl.Stops.OrderBy(s => s.Position).Select(s => new CrawlStopResponse(s.Position, s.BreweryId, s.BreweryName)).ToList());
}

public sealed record CrawlStopResponse(int Position, string BreweryId, string BreweryName);

// A date in the past in a request is invalid input: 400 (validation.in_the_past).
// Changing a crawl whose date has passed breaks a business rule: 422 (crawl.in_the_past).
public sealed record CreateCrawlRequest(string? Name, string? City, DateOnly? Date) : IValidatable
{
    public void Validate(ValidationErrors errors) => errors
        .Required("name", Name)
        .MaxLength("name", Name, 100)
        .Required("city", City)
        .MaxLength("city", City, 100)
        .Required("date", Date)
        .NotInPast("date", Date);
}

public sealed record UpdateCrawlRequest(string? Name, DateOnly? Date) : IValidatable
{
    public void Validate(ValidationErrors errors) => errors
        .Required("name", Name)
        .MaxLength("name", Name, 100)
        .Required("date", Date)
        .NotInPast("date", Date);
}

public sealed record AddStopRequest(string? BreweryId) : IValidatable
{
    public void Validate(ValidationErrors errors) => errors
        .Required("breweryId", BreweryId)
        .MaxLength("breweryId", BreweryId, 100);
}

using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;

namespace BreweryCrawl.Api.Features.Crawls;

/// <summary>
/// A planned pub crawl: a list of brewery stops in one city on one date.
/// Business rules live here and are returned as errors (not thrown), so callers must deal with them.
/// </summary>
public sealed class Crawl
{
    public const int MaxStops = 10;

    private static readonly string[] NotOpenTypes = ["closed", "planning"];

    public Crawl(string name, string city, DateOnly date, string ownerId)
    {
        Name = name;
        City = city;
        Date = date;
        OwnerId = ownerId;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; }
    public string City { get; private set; }
    public DateOnly Date { get; private set; }
    public string OwnerId { get; private set; }

    /// <summary>Incremented on every change. Used as ETag and as concurrency token in the database.</summary>
    public int Version { get; private set; } = 1;

    public List<CrawlStop> Stops { get; private set; } = [];

    public bool IsOwnedBy(string userId) => OwnerId == userId;

    public Error? Update(string name, DateOnly date, DateOnly today)
    {
        if (Date < today)
        {
            return CrawlErrors.InThePastError(this);
        }

        Name = name;
        Date = date;
        Version++;
        return null;
    }

    public Error? AddStop(Brewery brewery, DateOnly today)
    {
        if (Date < today)
        {
            return CrawlErrors.InThePastError(this);
        }

        if (Stops.Any(s => s.BreweryId == brewery.Id))
        {
            return CrawlErrors.DuplicateStopError(brewery);
        }

        if (Stops.Count >= MaxStops)
        {
            return CrawlErrors.TooManyStopsError();
        }

        if (!string.Equals(brewery.City, City, StringComparison.OrdinalIgnoreCase))
        {
            return CrawlErrors.WrongCityError(brewery, this);
        }

        if (NotOpenTypes.Contains(brewery.Type))
        {
            return CrawlErrors.BreweryNotOpenError(brewery);
        }

        Stops.Add(new CrawlStop(brewery.Id, brewery.Name, Stops.Count + 1));
        Version++;
        return null;
    }

    public Error? RemoveStop(string breweryId, DateOnly today)
    {
        if (Date < today)
        {
            return CrawlErrors.InThePastError(this);
        }

        var stop = Stops.FirstOrDefault(s => s.BreweryId == breweryId);
        if (stop is null)
        {
            return CrawlErrors.StopNotFoundError(breweryId);
        }

        Stops.Remove(stop);
        foreach (var (remaining, index) in Stops.OrderBy(s => s.Position).Select((s, i) => (s, i)))
        {
            remaining.Position = index + 1;
        }

        Version++;
        return null;
    }
}

public sealed class CrawlStop(string breweryId, string breweryName, int position)
{
    public string BreweryId { get; private set; } = breweryId;
    public string BreweryName { get; private set; } = breweryName;
    public int Position { get; set; } = position;
}

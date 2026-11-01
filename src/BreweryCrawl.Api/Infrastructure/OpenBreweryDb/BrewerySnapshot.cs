using System.Text.Json;
using BreweryCrawl.Api.Features.Breweries;

namespace BreweryCrawl.Api.Infrastructure.OpenBreweryDb;

/// <summary>
/// The full Open Brewery DB dataset bundled with the app (Data/breweries.json, MIT licensed,
/// from https://github.com/openbrewerydb/openbrewerydb). Used when the live API is down or in offline mode.
/// </summary>
public sealed class BrewerySnapshot
{
    private readonly IReadOnlyList<Brewery> _breweries;

    /// <summary>Loaded once at startup (see Program.cs), so a missing or broken file fails fast instead of mid-demo.</summary>
    public BrewerySnapshot(IWebHostEnvironment environment, ILogger<BrewerySnapshot> logger)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "breweries.json");
        using var stream = File.OpenRead(path);
        var breweries = JsonSerializer.Deserialize<List<OpenBreweryDbBrewery>>(stream, OpenBreweryDbBrewery.JsonOptions) ?? [];
        _breweries = breweries.Select(b => b.ToBrewery()).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
        logger.LogInformation("Loaded {Count} breweries from snapshot {Path}", _breweries.Count, path);
    }

    public IReadOnlyList<Brewery> List(BreweryFilter filter) =>
        _breweries
            .Where(b => filter.City is null || Contains(b.City, filter.City))
            .Where(b => filter.Name is null || Contains(b.Name, filter.Name))
            .Where(b => filter.Type is null || string.Equals(b.Type, filter.Type, StringComparison.OrdinalIgnoreCase))
            .Skip((filter.Page - 1) * filter.PerPage)
            .Take(filter.PerPage)
            .ToList();

    public Brewery? Get(string id) => _breweries.FirstOrDefault(b => b.Id == id);

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}

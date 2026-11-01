namespace BreweryCrawl.Api.Features.Breweries;

public sealed record Brewery(
    string Id,
    string Name,
    string Type,
    string? Street,
    string? City,
    string? StateProvince,
    string? PostalCode,
    string? Country,
    double? Latitude,
    double? Longitude,
    string? Phone,
    string? WebsiteUrl);

public sealed record BreweryPage(IReadOnlyList<Brewery> Items, int Page, int PerPage);

/// <summary>Filter for listing breweries. Same semantics for the live API and the snapshot.</summary>
public sealed record BreweryFilter(string? City, string? Name, string? Type, int Page, int PerPage);

public static class BreweryTypes
{
    public static readonly IReadOnlyList<string> All =
    [
        "micro", "nano", "regional", "brewpub", "large", "planning", "bar",
        "contract", "proprietor", "closed", "taproom", "cidery", "beergarden",
    ];
}

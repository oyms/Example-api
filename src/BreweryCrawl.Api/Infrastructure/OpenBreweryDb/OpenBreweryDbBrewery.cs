using System.Text.Json;
using System.Text.Json.Serialization;
using BreweryCrawl.Api.Features.Breweries;

namespace BreweryCrawl.Api.Infrastructure.OpenBreweryDb;

/// <summary>A brewery as returned by Open Brewery DB (and stored in the snapshot file).</summary>
public sealed record OpenBreweryDbBrewery(
    string Id,
    string Name,
    string BreweryType,
    [property: JsonPropertyName("address_1")] string? Address1,
    string? City,
    string? StateProvince,
    string? PostalCode,
    string? Country,
    double? Latitude,
    double? Longitude,
    string? Phone,
    string? WebsiteUrl)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Brewery ToBrewery() => new(
        Id, Name, BreweryType, Address1, City, StateProvince, PostalCode, Country,
        Latitude, Longitude, Phone, WebsiteUrl);
}

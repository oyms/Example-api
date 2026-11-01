using System.Net;
using System.Text.Json;

namespace BreweryCrawl.Api.Tests;

public sealed class BreweryEndpointsTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    private const string KnownBreweryId = "e432899b-7f58-455f-9c7b-9a6e2130a1e0";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task List_returns_breweries_from_snapshot()
    {
        var response = await _client.GetAsync("/api/breweries?city=portland&perPage=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("snapshot", response.Headers.GetValues("X-Data-Source").Single());
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, page.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task List_with_invalid_query_returns_validation_problem_with_codes_per_field()
    {
        var response = await _client.GetAsync("/api/breweries?perPage=500&type=foo");

        var problem = await response.AssertProblem(HttpStatusCode.BadRequest, "validation.failed");
        Assert.True(problem.GetProperty("errors").TryGetProperty("perPage", out _));
        var perPage = problem.GetProperty("errorCodes").GetProperty("perPage")[0];
        Assert.Equal("validation.out_of_range", perPage.GetProperty("code").GetString());
        Assert.Equal(50, perPage.GetProperty("params").GetProperty("max").GetInt32());
        Assert.Equal("validation.invalid_value",
            problem.GetProperty("errorCodes").GetProperty("type")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_with_unparsable_query_returns_malformed_request()
    {
        var response = await _client.GetAsync("/api/breweries?perPage=abc");

        await response.AssertProblem(HttpStatusCode.BadRequest, "request.malformed");
    }

    [Fact]
    public async Task Get_returns_brewery()
    {
        var response = await _client.GetAsync($"/api/breweries/{KnownBreweryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var brewery = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("10 Barrel Brewing Co", brewery.GetProperty("name").GetString());
        Assert.Equal("1411 NW Flanders St", brewery.GetProperty("street").GetString());
    }

    [Fact]
    public async Task Get_unknown_brewery_returns_not_found_with_params()
    {
        var response = await _client.GetAsync("/api/breweries/does-not-exist");

        var problem = await response.AssertProblem(HttpStatusCode.NotFound, "brewery.not_found");
        Assert.Equal("does-not-exist", problem.GetProperty("params").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unknown_route_returns_problem()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        await response.AssertProblem(HttpStatusCode.NotFound, "request.not_found");
    }

    [Fact]
    public async Task Wrong_method_returns_problem()
    {
        var response = await _client.DeleteAsync("/api/breweries");

        await response.AssertProblem(HttpStatusCode.MethodNotAllowed, "request.method_not_allowed");
    }

    [Fact]
    public async Task Error_catalog_lists_all_error_codes()
    {
        var catalog = await _client.GetFromJsonAsync<JsonElement>("/errors");

        var codes = catalog.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        Assert.Contains("brewery.not_found", codes);
        Assert.Contains("upstream.timeout", codes);
    }
}

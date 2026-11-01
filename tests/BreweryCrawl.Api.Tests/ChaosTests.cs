using System.Net;

namespace BreweryCrawl.Api.Tests;

public sealed class ChaosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> GetWithChaos(string chaos, string url = "/api/breweries")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Chaos", chaos);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Fail_returns_500_without_leaking_exception_details()
    {
        var response = await GetWithChaos("fail");

        var problem = await response.AssertProblem(HttpStatusCode.InternalServerError, "server.unexpected");
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.DoesNotContain("Simulated", problem.GetRawText());
    }

    [Fact]
    public async Task Errors_are_problem_json_even_when_client_asks_for_html()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/breweries?chaos=fail");
        request.Headers.Add("Accept", "text/html");

        var response = await _client.SendAsync(request);

        await response.AssertProblem(HttpStatusCode.InternalServerError, "server.unexpected");
    }

    [Fact]
    public async Task Unknown_chaos_mode_returns_validation_problem()
    {
        var response = await _client.GetAsync("/api/breweries?chaos=banana");

        await response.AssertProblem(HttpStatusCode.BadRequest, "validation.failed");
    }

    [Fact]
    public async Task Upstream_down_falls_back_to_snapshot()
    {
        var response = await GetWithChaos("upstream-down", "/api/breweries?city=portland");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("snapshot", response.Headers.GetValues("X-Data-Source").Single());
    }

    [Fact]
    public async Task Upstream_down_without_fallback_returns_502()
    {
        var response = await GetWithChaos("upstream-down,no-fallback");

        await response.AssertProblem(HttpStatusCode.BadGateway, "upstream.unavailable");
    }

    [Fact]
    public async Task Upstream_circuit_open_without_fallback_returns_503()
    {
        var response = await GetWithChaos("upstream-circuit-open,no-fallback");

        await response.AssertProblem(HttpStatusCode.ServiceUnavailable, "upstream.circuit_open");
    }

    [Fact]
    public async Task Error_responses_include_cors_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/breweries");
        request.Headers.Add("X-Chaos", "fail");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}

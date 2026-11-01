using System.Net;

namespace BreweryCrawl.Api.Tests;

public sealed class UpstreamTests
{
    private const string OneBrewery =
        """[{"id":"abc","name":"Stub Brewing","brewery_type":"micro","address_1":"Main St 1","city":"Portland"}]""";

    private static HttpRequestMessage WithChaos(string url, string chaos)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Chaos", chaos);
        return request;
    }

    [Fact]
    public async Task Second_identical_request_is_served_from_cache()
    {
        using var factory = new StubbedUpstreamFactory("Live");
        factory.Upstream.Respond = _ => StubUpstream.Json(OneBrewery);
        var client = factory.CreateClient();

        var first = await client.GetAsync("/api/breweries?city=portland");
        var second = await client.GetAsync("/api/breweries?city=portland");

        Assert.Equal("live", first.Headers.GetValues("X-Data-Source").Single());
        Assert.Equal("cache", second.Headers.GetValues("X-Data-Source").Single());
        Assert.Equal(1, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Different_filters_are_cached_separately()
    {
        using var factory = new StubbedUpstreamFactory("Live");
        var client = factory.CreateClient();

        // These two would collide if the cache key were built from a formatted string.
        await client.GetAsync("/api/breweries?city=a%2C%20Name%20%3D%20b");
        await client.GetAsync("/api/breweries?city=a&name=b%2C%20Name%20%3D%20");

        Assert.Equal(2, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Upstream_404_becomes_brewery_not_found()
    {
        using var factory = new StubbedUpstreamFactory("Live");
        factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var response = await factory.CreateClient().GetAsync("/api/breweries/abc");

        await response.AssertProblem(HttpStatusCode.NotFound, "brewery.not_found");
    }

    [Fact]
    public async Task Unreadable_upstream_response_becomes_502()
    {
        using var factory = new StubbedUpstreamFactory("Live");
        factory.Upstream.Respond = _ => StubUpstream.Json("<html>oops</html>");

        var response = await factory.CreateClient().GetAsync("/api/breweries");

        await response.AssertProblem(HttpStatusCode.BadGateway, "upstream.unavailable");
    }

    [Fact]
    public async Task Real_upstream_failure_falls_back_to_snapshot()
    {
        using var factory = new StubbedUpstreamFactory("LiveWithFallback");
        factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var response = await factory.CreateClient().GetAsync("/api/breweries?city=portland");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("snapshot", response.Headers.GetValues("X-Data-Source").Single());
    }

    [Fact]
    public async Task Repeated_real_failures_open_the_circuit_breaker()
    {
        using var factory = new StubbedUpstreamFactory("Live");
        factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            statuses.Add((await client.GetAsync($"/api/breweries?page={i + 1}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadGateway, statuses[0]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, statuses[^1]);
    }

    [Fact]
    public async Task Simulated_failures_do_not_affect_the_real_circuit_breaker()
    {
        using var factory = new StubbedUpstreamFactory("Live");
        var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            await client.SendAsync(WithChaos($"/api/breweries?page={i + 1}", "upstream-down"));
        }

        var response = await client.GetAsync("/api/breweries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("live", response.Headers.GetValues("X-Data-Source").Single());
        Assert.Equal(1, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Upstream_timeout_without_fallback_returns_504()
    {
        using var factory = new StubbedUpstreamFactory("LiveWithFallback");

        var response = await factory.CreateClient().SendAsync(WithChaos("/api/breweries", "upstream-timeout,no-fallback"));

        await response.AssertProblem(HttpStatusCode.GatewayTimeout, "upstream.timeout");
    }

    [Fact]
    public async Task Upstream_chaos_also_works_in_offline_mode()
    {
        using var factory = new StubbedUpstreamFactory("Offline");

        var response = await factory.CreateClient().SendAsync(WithChaos("/api/breweries", "upstream-down,no-fallback"));

        await response.AssertProblem(HttpStatusCode.BadGateway, "upstream.unavailable");
        Assert.Equal(0, factory.Upstream.Calls);
    }

    [Fact]
    public async Task Upstream_chaos_on_get_by_id_falls_back_to_snapshot()
    {
        using var factory = new StubbedUpstreamFactory("LiveWithFallback");

        var response = await factory.CreateClient().SendAsync(
            WithChaos("/api/breweries/e432899b-7f58-455f-9c7b-9a6e2130a1e0", "upstream-down"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("snapshot", response.Headers.GetValues("X-Data-Source").Single());
    }
}

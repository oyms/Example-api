using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BreweryCrawl.Api.Tests;

public sealed class CrawlTests(OfflineApiFactory factory) : IClassFixture<OfflineApiFactory>
{
    // Portland breweries from the snapshot.
    private const string ThirteenVirtues = "936c3d7e-5d54-4459-b72c-117cdda059b4";
    private const string Brewery26 = "cfac009a-caae-4462-beb3-1f603674e2a6";
    private const string ClosedBrewery = "08155729-56b6-4a82-a43e-5b458dd01c2e";
    private const string SeattleBrewery = "9c9090eb-a361-47e7-b2e5-06fefe21eef7";

    private readonly HttpClient _client = factory.CreateClient();

    private static HttpRequestMessage As(string user, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-User", user);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<JsonElement> CreateCrawl(string user, string date = "2099-06-01")
    {
        var response = await _client.SendAsync(As(user, HttpMethod.Post, "/api/crawls",
            new { name = "Test crawl", city = "Portland", date }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> AddStop(string user, JsonElement crawl, string breweryId) =>
        await _client.SendAsync(As(user, HttpMethod.Post, $"/api/crawls/{crawl.GetProperty("id")}/stops", new { breweryId }));

    [Fact]
    public async Task Create_returns_201_with_location_and_etag()
    {
        var response = await _client.SendAsync(As("carol", HttpMethod.Post, "/api/crawls",
            new { name = "Carol's crawl", city = "Portland", date = "2099-01-01" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Create_without_user_returns_401()
    {
        var response = await _client.PostAsJsonAsync("/api/crawls", new { name = "x", city = "Portland", date = "2099-01-01" });

        await response.AssertProblem(HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Equal("X-User", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task Create_with_invalid_body_returns_validation_problem()
    {
        var response = await _client.SendAsync(As("carol", HttpMethod.Post, "/api/crawls", new { name = "", date = "2000-01-01" }));

        var problem = await response.AssertProblem(HttpStatusCode.BadRequest, "validation.failed");
        var codes = problem.GetProperty("errorCodes");
        Assert.Equal("validation.required", codes.GetProperty("name")[0].GetProperty("code").GetString());
        Assert.Equal("validation.required", codes.GetProperty("city")[0].GetProperty("code").GetString());
        Assert.Equal("validation.in_the_past", codes.GetProperty("date")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_with_broken_json_returns_malformed_request()
    {
        var request = As("carol", HttpMethod.Post, "/api/crawls");
        request.Content = new StringContent("{\"name\":", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        await response.AssertProblem(HttpStatusCode.BadRequest, "request.malformed");
    }

    [Fact]
    public async Task Changing_someone_elses_crawl_returns_403()
    {
        var crawl = await CreateCrawl("alice");

        var response = await AddStop("mallory", crawl, ThirteenVirtues);

        await response.AssertProblem(HttpStatusCode.Forbidden, "crawl.not_owner");
    }

    [Fact]
    public async Task Unknown_crawl_returns_404()
    {
        var response = await _client.GetAsync($"/api/crawls/{Guid.NewGuid()}");

        await response.AssertProblem(HttpStatusCode.NotFound, "crawl.not_found");
    }

    [Fact]
    public async Task Adding_the_same_brewery_twice_returns_409()
    {
        var crawl = await CreateCrawl("alice");
        await AddStop("alice", crawl, ThirteenVirtues);

        var response = await AddStop("alice", crawl, ThirteenVirtues);

        await response.AssertProblem(HttpStatusCode.Conflict, "crawl.duplicate_stop");
    }

    [Theory]
    [InlineData(SeattleBrewery, "crawl.brewery_wrong_city")]
    [InlineData(ClosedBrewery, "crawl.brewery_not_open")]
    [InlineData("does-not-exist", "crawl.unknown_brewery")]
    public async Task Breaking_a_business_rule_returns_422(string breweryId, string errorCode)
    {
        var crawl = await CreateCrawl("alice");

        var response = await AddStop("alice", crawl, breweryId);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, errorCode);
    }

    [Fact]
    public async Task Changing_a_crawl_in_the_past_returns_422()
    {
        var crawls = await _client.GetFromJsonAsync<JsonElement>("/api/crawls");
        var past = crawls.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Last Summer's Crawl");

        var response = await AddStop("alice", past, Brewery26);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "crawl.in_the_past");
    }

    [Fact]
    public async Task Update_with_stale_etag_returns_412_and_with_current_etag_succeeds()
    {
        var crawl = await CreateCrawl("alice");
        var url = $"/api/crawls/{crawl.GetProperty("id")}";
        await AddStop("alice", crawl, ThirteenVirtues); // Version 1 → 2

        var stale = As("alice", HttpMethod.Put, url, new { name = "Renamed", date = "2099-07-01" });
        stale.Headers.IfMatch.Add(new EntityTagHeaderValue("\"1\""));
        var staleResponse = await _client.SendAsync(stale);

        var problem = await staleResponse.AssertProblem(HttpStatusCode.PreconditionFailed, "crawl.version_mismatch");
        Assert.Equal(2, problem.GetProperty("params").GetProperty("currentVersion").GetInt32());

        var current = As("alice", HttpMethod.Put, url, new { name = "Renamed", date = "2099-07-01" });
        current.Headers.IfMatch.Add(new EntityTagHeaderValue("\"2\""));
        var currentResponse = await _client.SendAsync(current);

        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal("\"3\"", currentResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Removing_a_stop_renumbers_the_rest()
    {
        var crawl = await CreateCrawl("alice");
        await AddStop("alice", crawl, ThirteenVirtues);
        await AddStop("alice", crawl, Brewery26);

        var response = await _client.SendAsync(As("alice", HttpMethod.Delete,
            $"/api/crawls/{crawl.GetProperty("id")}/stops/{ThirteenVirtues}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<JsonElement>($"/api/crawls/{crawl.GetProperty("id")}");
        var stop = Assert.Single(updated.GetProperty("stops").EnumerateArray());
        Assert.Equal(1, stop.GetProperty("position").GetInt32());
        Assert.Equal(Brewery26, stop.GetProperty("breweryId").GetString());
    }

    [Fact]
    public async Task Upstream_failure_while_adding_a_stop_returns_502()
    {
        var crawl = await CreateCrawl("alice");
        var request = As("alice", HttpMethod.Post, $"/api/crawls/{crawl.GetProperty("id")}/stops", new { breweryId = Brewery26 });
        request.Headers.Add("X-Chaos", "upstream-down,no-fallback");

        var response = await _client.SendAsync(request);

        await response.AssertProblem(HttpStatusCode.BadGateway, "upstream.unavailable");
    }

    [Fact]
    public async Task Adding_the_same_brewery_at_the_same_time_never_returns_500()
    {
        var crawl = await CreateCrawl("alice");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => AddStop("alice", crawl, ThirteenVirtues)));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Single(statuses, HttpStatusCode.OK);
        Assert.All(statuses, status => Assert.Contains(status, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
    }

    [Fact]
    public async Task Adding_an_11th_stop_returns_422()
    {
        var crawl = await CreateCrawl("alice");
        var snapshot = await _client.GetFromJsonAsync<JsonElement>("/api/breweries?city=Portland&type=micro&perPage=11");
        var ids = snapshot.GetProperty("items").EnumerateArray()
            .Where(b => b.GetProperty("city").GetString() == "Portland")
            .Select(b => b.GetProperty("id").GetString()!)
            .ToList();
        Assert.True(ids.Count >= 11);
        foreach (var id in ids.Take(10))
        {
            Assert.Equal(HttpStatusCode.OK, (await AddStop("alice", crawl, id)).StatusCode);
        }

        var response = await AddStop("alice", crawl, ids[10]);

        var problem = await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "crawl.too_many_stops");
        Assert.Equal(10, problem.GetProperty("params").GetProperty("maxStops").GetInt32());
    }

    [Fact]
    public async Task Removing_a_stop_that_is_not_there_returns_404()
    {
        var crawl = await CreateCrawl("alice");

        var response = await _client.SendAsync(As("alice", HttpMethod.Delete,
            $"/api/crawls/{crawl.GetProperty("id")}/stops/{Brewery26}"));

        await response.AssertProblem(HttpStatusCode.NotFound, "crawl.stop_not_found");
    }

    [Fact]
    public async Task Delete_returns_204_for_the_owner_and_403_for_others()
    {
        var crawl = await CreateCrawl("alice");
        var url = $"/api/crawls/{crawl.GetProperty("id")}";

        var forbidden = await _client.SendAsync(As("mallory", HttpMethod.Delete, url));
        var deleted = await _client.SendAsync(As("alice", HttpMethod.Delete, url));
        var afterwards = await _client.GetAsync(url);

        await forbidden.AssertProblem(HttpStatusCode.Forbidden, "crawl.not_owner");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await afterwards.AssertProblem(HttpStatusCode.NotFound, "crawl.not_found");
    }

    [Fact]
    public async Task User_names_are_case_insensitive()
    {
        var crawl = await CreateCrawl("alice");

        var response = await AddStop("Alice", crawl, ThirteenVirtues);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_user_header_returns_401_with_explanation()
    {
        var response = await _client.SendAsync(As("not a name!", HttpMethod.Post, "/api/crawls",
            new { name = "x", city = "Portland", date = "2099-01-01" }));

        var problem = await response.AssertProblem(HttpStatusCode.Unauthorized, "auth.unauthenticated");
        Assert.Contains("X-User", problem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("\"1\", \"2\"", HttpStatusCode.OK)] // A list matches if any entry matches.
    [InlineData("*", HttpStatusCode.OK)]
    [InlineData("W/\"1\"", HttpStatusCode.PreconditionFailed)] // Weak ETags never match If-Match.
    [InlineData("\"7\"", HttpStatusCode.PreconditionFailed)]
    public async Task If_match_follows_http_rules(string ifMatch, HttpStatusCode expected)
    {
        var crawl = await CreateCrawl("alice"); // Version 1
        var request = As("alice", HttpMethod.Put, $"/api/crawls/{crawl.GetProperty("id")}", new { name = "Renamed", date = "2099-07-01" });
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        var response = await _client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }
}

public sealed class RateLimitTests
{
    [Fact]
    public async Task Creating_too_many_crawls_returns_429_with_retry_after()
    {
        using var factory = new StrictRateLimitApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User", "speedy");
        var body = new { name = "x", city = "Portland", date = "2099-01-01" };

        await client.PostAsJsonAsync("/api/crawls", body);
        var response = await client.PostAsJsonAsync("/api/crawls", body);

        await response.AssertProblem(HttpStatusCode.TooManyRequests, "request.rate_limited");
        Assert.NotNull(response.Headers.RetryAfter);
    }
}

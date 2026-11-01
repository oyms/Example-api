using System.Net;
using BreweryCrawl.Api.Infrastructure.OpenBreweryDb;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BreweryCrawl.Api.Tests;

/// <summary>Replaces the network call to Open Brewery DB with a configurable stub.</summary>
public sealed class StubUpstream : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Json("[]");

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Respond(request));
    }
}

/// <summary>
/// API with a stubbed upstream and short timeouts. Create one per test, since the circuit breaker
/// and the cache keep state for the lifetime of the app.
/// </summary>
public sealed class StubbedUpstreamFactory(string dataSource) : TestApiFactory(dataSource)
{
    public StubUpstream Upstream { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("OpenBreweryDb:AttemptTimeout", "00:00:00.200");
        builder.UseSetting("OpenBreweryDb:TotalTimeout", "00:00:02");
        builder.ConfigureTestServices(services => services
            .AddHttpClient<OpenBreweryDbClient>()
            .ConfigurePrimaryHttpMessageHandler(() => Upstream));
    }
}

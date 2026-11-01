using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BreweryCrawl.Api.Tests;

/// <summary>
/// Runs the API in-memory as in production. Uses the live data source mode, but tests only reach
/// Open Brewery DB through chaos modes that short-circuit the call, or read from the snapshot.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Breweries:DataSource", "LiveWithFallback");
    }
}

public sealed class OfflineApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Breweries:DataSource", "Offline");
    }
}

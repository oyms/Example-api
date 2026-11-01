using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace BreweryCrawl.Api.Tests;

/// <summary>
/// Runs the API in-memory as in production, with its own SQLite database file
/// and a rate limit high enough not to get in the way (unless a test overrides it).
/// </summary>
public abstract class TestApiFactory(string dataSource, IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"crawls-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Breweries:DataSource", dataSource);
        builder.UseSetting("ConnectionStrings:Crawls", $"Data Source={_databasePath}");
        builder.UseSetting("RateLimiting:CreateCrawl:PermitLimit", "1000");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}

/// <summary>Live data source with fallback. Tests only reach Open Brewery DB through chaos modes that short-circuit the call.</summary>
public sealed class ApiFactory() : TestApiFactory("LiveWithFallback");

/// <summary>Uses only the bundled snapshot. No network.</summary>
public sealed class OfflineApiFactory() : TestApiFactory("Offline");

/// <summary>Offline, with a limit of one created crawl per minute.</summary>
public sealed class StrictRateLimitApiFactory()
    : TestApiFactory("Offline", new Dictionary<string, string> { ["RateLimiting:CreateCrawl:PermitLimit"] = "1" });

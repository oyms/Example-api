using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Features.Crawls;
using BreweryCrawl.Api.Infrastructure.OpenBreweryDb;
using Microsoft.EntityFrameworkCore;

namespace BreweryCrawl.Api.Infrastructure.Persistence;

public static class DatabaseSetup
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        // Default: crawls.db next to the project (or the published app), whatever the working directory is.
        var connectionString = configuration.GetConnectionString("Crawls")
            ?? $"Data Source={Path.Combine(environment.ContentRootPath, "crawls.db")}";
        services.AddDbContext<CrawlDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<CrawlSeeder>();
        return services;
    }

    /// <summary>
    /// Creates the SQLite database with example data if it doesn't exist. No migrations: this is a workshop app.
    /// If the model changes, an existing database is not updated: call POST /admin/reset or delete crawls.db.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlDbContext>();
        if (await db.Database.EnsureCreatedAsync())
        {
            await scope.ServiceProvider.GetRequiredService<CrawlSeeder>().SeedAsync();
        }
    }

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/reset", async (CrawlDbContext db, CrawlSeeder seeder, ILogger<CrawlSeeder> logger) =>
            {
                await db.Database.EnsureDeletedAsync();
                await db.Database.EnsureCreatedAsync();
                await seeder.SeedAsync();
                logger.LogWarning("Database was reset to the example data");
                return TypedResults.NoContent();
            })
            .WithTags("Admin")
            .WithSummary("Reset all crawls to the example data");

        return app;
    }
}

/// <summary>Example crawls, so there is something to look at (and someone else's crawl to get a 403 on).</summary>
public sealed class CrawlSeeder(CrawlDbContext db, BrewerySnapshot snapshot, TimeProvider timeProvider)
{
    public async Task SeedAsync()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        db.Crawls.AddRange(
            Create("Pearl District Pints", "alice", today.AddDays(14),
                "936c3d7e-5d54-4459-b72c-117cdda059b4", "45249b6e-b1d0-4c2f-a60e-ab483169287f", "10ad1074-00f1-4731-9154-878dafcb58a9"),
            Create("Bob's Big Night Out", "bob", today.AddDays(30),
                "b7db853a-e8aa-430f-84c8-d49b72184d01", "d53226dc-b8b1-494c-85f5-1c97ba255947"),
            Create("Last Summer's Crawl", "alice", today.AddDays(-60),
                "c4e818fa-6abc-440b-bdb5-c8172bb5fbd8"));

        await db.SaveChangesAsync();
    }

    private Crawl Create(string name, string ownerId, DateOnly date, params string[] breweryIds)
    {
        var crawl = new Crawl(name, "Portland", date, ownerId);
        foreach (var brewery in breweryIds.Select(id => snapshot.Get(id)).OfType<Brewery>())
        {
            // DateOnly.MinValue as "today", so stops can also be added to the crawl that is in the past.
            crawl.AddStop(brewery, DateOnly.MinValue);
        }

        return crawl;
    }
}

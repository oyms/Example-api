using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using BreweryCrawl.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BreweryCrawl.Api.Features.Crawls;

/// <summary>
/// Use cases for crawls. Expected problems (not found, not owner, business rules) are returned as errors;
/// unexpected ones (database down, upstream failures) are thrown and handled by GlobalExceptionHandler.
/// </summary>
public sealed class CrawlService(
    CrawlDbContext db,
    BreweryCatalog breweries,
    TimeProvider timeProvider,
    ILogger<CrawlService> logger)
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<Crawl>> ListAsync(string? city, CancellationToken cancellationToken)
    {
        var query = db.Crawls.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(city))
        {
            query = query.Where(c => c.City.ToLower() == city.ToLower());
        }

        return await query.OrderBy(c => c.Date).ToListAsync(cancellationToken);
    }

    public async Task<Result<Crawl>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Crawls.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } crawl
            ? crawl
            : CrawlErrors.NotFoundError(id);

    public async Task<Crawl> CreateAsync(CreateCrawlRequest request, string userName, CancellationToken cancellationToken)
    {
        var crawl = new Crawl(request.Name!.Trim(), request.City!.Trim(), request.Date!.Value, userName);
        db.Crawls.Add(crawl);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("{User} created crawl {CrawlId} '{CrawlName}'", userName, crawl.Id, crawl.Name);
        return crawl;
    }

    public async Task<Result<Crawl>> UpdateAsync(
        Guid id, UpdateCrawlRequest request, string userName, IReadOnlyCollection<int>? expectedVersions, CancellationToken cancellationToken)
    {
        var loaded = await LoadForChangeAsync(id, userName, expectedVersions, cancellationToken);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var crawl = loaded.Value;
        if (crawl.Update(request.Name!.Trim(), request.Date!.Value, Today) is { } error)
        {
            return error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return crawl;
    }

    public async Task<Result<Crawl>> DeleteAsync(Guid id, string userName, IReadOnlyCollection<int>? expectedVersions, CancellationToken cancellationToken)
    {
        var loaded = await LoadForChangeAsync(id, userName, expectedVersions, cancellationToken);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        db.Crawls.Remove(loaded.Value);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("{User} deleted crawl {CrawlId}", userName, id);
        return loaded.Value;
    }

    public async Task<Result<Crawl>> AddStopAsync(
        Guid id, AddStopRequest request, string userName, IReadOnlyCollection<int>? expectedVersions, CancellationToken cancellationToken)
    {
        var loaded = await LoadForChangeAsync(id, userName, expectedVersions, cancellationToken);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        // May call Open Brewery DB, so upstream failures (502/503/504) can also happen here.
        var breweryId = request.BreweryId!.Trim();
        var brewery = await breweries.GetAsync(breweryId, cancellationToken);
        if (!brewery.IsSuccess)
        {
            // The brewery is part of the request body, not the URL, so this is a 422 rather than a 404.
            return CrawlErrors.UnknownBreweryError(breweryId);
        }

        var crawl = loaded.Value;
        if (crawl.AddStop(brewery.Value.Value, Today) is { } error)
        {
            logger.LogInformation("Could not add {BreweryId} to crawl {CrawlId}: {ErrorCode}",
                breweryId, id, error.Definition.Code);
            return error;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Two requests added the same brewery at the same time (e.g. a double-click). The check in
            // Crawl.AddStop passed for both, but the database's unique key stopped the second one.
            return CrawlErrors.DuplicateStopError(brewery.Value.Value);
        }

        return crawl;
    }

    public async Task<Result<Crawl>> RemoveStopAsync(
        Guid id, string breweryId, string userName, IReadOnlyCollection<int>? expectedVersions, CancellationToken cancellationToken)
    {
        var loaded = await LoadForChangeAsync(id, userName, expectedVersions, cancellationToken);
        if (loaded.Error is { } loadError)
        {
            return loadError;
        }

        var crawl = loaded.Value;
        if (crawl.RemoveStop(breweryId, Today) is { } error)
        {
            return error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return crawl;
    }

    /// <summary>
    /// The checks every change goes through, in order: exists (404), owned by the user (403), not stale (412).
    /// <paramref name="expectedVersions"/> come from If-Match. Null means no If-Match: last write wins.
    /// </summary>
    private async Task<Result<Crawl>> LoadForChangeAsync(
        Guid id, string userName, IReadOnlyCollection<int>? expectedVersions, CancellationToken cancellationToken)
    {
        var crawl = await db.Crawls.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (crawl is null)
        {
            return CrawlErrors.NotFoundError(id);
        }

        // Crawls are public, so 403 doesn't reveal anything. For private data, 404 would hide that it exists.
        if (!crawl.IsOwnedBy(userName))
        {
            logger.LogInformation("{User} tried to change crawl {CrawlId} owned by {Owner}", userName, id, crawl.OwnerId);
            return CrawlErrors.NotOwnerError(crawl);
        }

        if (expectedVersions is not null && !expectedVersions.Contains(crawl.Version))
        {
            return CrawlErrors.VersionMismatchError(crawl.Version);
        }

        return crawl;
    }
}

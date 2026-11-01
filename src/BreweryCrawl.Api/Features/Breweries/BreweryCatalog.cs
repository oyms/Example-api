using BreweryCrawl.Api.Infrastructure.Chaos;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using BreweryCrawl.Api.Infrastructure.OpenBreweryDb;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BreweryCrawl.Api.Features.Breweries;

public enum BreweryDataSourceMode
{
    /// <summary>Only the live API. Upstream failures reach the client as 502/503/504.</summary>
    Live,

    /// <summary>The live API, falling back to the bundled snapshot when it fails.</summary>
    LiveWithFallback,

    /// <summary>Only the bundled snapshot. For running without internet.</summary>
    Offline,
}

public sealed class BreweryOptions
{
    public BreweryDataSourceMode DataSource { get; set; } = BreweryDataSourceMode.LiveWithFallback;
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>Where the data in a response came from. Returned to the client in the X-Data-Source header.</summary>
public enum DataSource { Live, Cache, Snapshot }

public sealed record Sourced<T>(T Value, DataSource Source);

/// <summary>
/// Reads breweries from Open Brewery DB with caching, and falls back to the bundled snapshot
/// when the live API fails (depending on <see cref="BreweryOptions.DataSource"/>).
/// </summary>
public sealed class BreweryCatalog(
    OpenBreweryDbClient client,
    BrewerySnapshot snapshot,
    IMemoryCache cache,
    IOptions<BreweryOptions> options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<BreweryCatalog> logger)
{
    public Task<Sourced<IReadOnlyList<Brewery>>> ListAsync(BreweryFilter filter, CancellationToken cancellationToken) =>
        FetchAsync(filter, ct => client.ListAsync(filter, ct), () => snapshot.List(filter), cancellationToken);

    public async Task<Result<Sourced<Brewery>>> GetAsync(string id, CancellationToken cancellationToken)
    {
        var (brewery, source) = await FetchAsync(("brewery", id), ct => client.GetAsync(id, ct), () => snapshot.Get(id), cancellationToken);
        return brewery is null
            ? BreweryErrors.NotFoundError(id)
            : new Sourced<Brewery>(brewery, source);
    }

    private async Task<Sourced<T>> FetchAsync<T>(
        object cacheKey,
        Func<CancellationToken, Task<T>> fetchLive,
        Func<T> fetchSnapshot,
        CancellationToken cancellationToken)
    {
        var mode = options.Value.DataSource;
        var chaos = Chaos.Get(httpContextAccessor.HttpContext);

        // Upstream chaos still "calls" the upstream in Offline mode (the call is simulated, no network needed),
        // so the 502/503/504 demos also work without internet.
        if (mode == BreweryDataSourceMode.Offline && !chaos.AffectsUpstream())
        {
            return new Sourced<T>(fetchSnapshot(), DataSource.Snapshot);
        }

        // Simulated upstream faults should hit the upstream, not be hidden by the cache.
        if (!chaos.AffectsUpstream() && cache.TryGetValue(cacheKey, out T? cached))
        {
            return new Sourced<T>(cached!, DataSource.Cache);
        }

        try
        {
            var value = await fetchLive(cancellationToken);
            cache.Set(cacheKey, value, options.Value.CacheDuration);
            return new Sourced<T>(value, DataSource.Live);
        }
        catch (UpstreamException ex) when (mode != BreweryDataSourceMode.Live && !chaos.HasFlag(ChaosMode.NoFallback))
        {
            // Graceful degradation: the client gets (possibly stale) data instead of an error.
            logger.LogWarning("{Service} failed ({Failure}: {Reason}), serving data from snapshot instead",
                ex.Service, ex.Failure, ex.Message);
            return new Sourced<T>(fetchSnapshot(), DataSource.Snapshot);
        }
    }
}

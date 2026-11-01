using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using Microsoft.AspNetCore.WebUtilities;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BreweryCrawl.Api.Infrastructure.OpenBreweryDb;

/// <summary>
/// Typed client for https://api.openbrewerydb.org. Translates every kind of failure (HTTP errors, timeouts,
/// open circuit, unreadable JSON) into an <see cref="UpstreamException"/>, so callers only have one thing to handle.
/// </summary>
public sealed class OpenBreweryDbClient(HttpClient http, ILogger<OpenBreweryDbClient> logger)
{
    public const string ServiceName = "Open Brewery DB";

    public Task<IReadOnlyList<Brewery>> ListAsync(BreweryFilter filter, CancellationToken cancellationToken)
    {
        var url = QueryHelpers.AddQueryString("breweries", new Dictionary<string, string?>
        {
            ["by_city"] = filter.City,
            ["by_name"] = filter.Name,
            ["by_type"] = filter.Type,
            ["page"] = filter.Page.ToString(),
            ["per_page"] = filter.PerPage.ToString(),
            ["sort"] = "name:asc",
        });

        return SendAsync(url, async ct =>
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var breweries = await response.Content.ReadFromJsonAsync<List<OpenBreweryDbBrewery>>(OpenBreweryDbBrewery.JsonOptions, ct);
            return (IReadOnlyList<Brewery>)(breweries ?? []).Select(b => b.ToBrewery()).ToList();
        }, cancellationToken);
    }

    /// <returns>The brewery, or null if Open Brewery DB says it does not exist.</returns>
    public Task<Brewery?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var url = $"breweries/{Uri.EscapeDataString(id)}";

        return SendAsync(url, async ct =>
        {
            using var response = await http.GetAsync(url, ct);

            // Open Brewery DB answers unknown ids with an HTML 404 page. "Not found" is an expected answer, not a failure.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var brewery = await response.Content.ReadFromJsonAsync<OpenBreweryDbBrewery>(OpenBreweryDbBrewery.JsonOptions, ct);
            return brewery?.ToBrewery();
        }, cancellationToken);
    }

    private async Task<T> SendAsync<T>(string url, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await call(cancellationToken);
            logger.LogInformation("{Service} GET {Url} succeeded in {ElapsedMs} ms", ServiceName, url, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (TimeoutRejectedException ex)
        {
            throw new UpstreamException(ServiceName, UpstreamFailure.Timeout,
                $"{ServiceName} GET {url} timed out after {stopwatch.ElapsedMilliseconds} ms", ex);
        }
        catch (BrokenCircuitException ex)
        {
            throw new UpstreamException(ServiceName, UpstreamFailure.CircuitOpen,
                $"{ServiceName} GET {url} was not attempted because the circuit breaker is open", ex);
        }
        catch (ExecutionRejectedException ex)
        {
            throw new UpstreamException(ServiceName, UpstreamFailure.Unavailable,
                $"{ServiceName} GET {url} was rejected by the resilience pipeline: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpstreamException(ServiceName, UpstreamFailure.Unavailable,
                $"{ServiceName} GET {url} failed with {(ex.StatusCode is { } status ? $"status {(int)status}" : ex.HttpRequestError)}", ex);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new UpstreamException(ServiceName, UpstreamFailure.Unavailable,
                $"{ServiceName} GET {url} returned a response we could not read", ex);
        }
    }
}

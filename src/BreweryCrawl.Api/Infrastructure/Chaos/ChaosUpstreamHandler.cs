using System.Net;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace BreweryCrawl.Api.Infrastructure.Chaos;

/// <summary>
/// Simulates Open Brewery DB failures. Sits outermost in the HttpClient pipeline, outside the resilience handler,
/// and returns what the resilience handler would have produced after giving up. That way simulated faults never
/// reach the real circuit breaker, so one participant's chaos can't open (or close!) it for everyone.
/// Real failures still go through retries, timeouts and the circuit breaker.
/// </summary>
public sealed class ChaosUpstreamHandler(
    IHttpContextAccessor httpContextAccessor,
    TimeSpan simulatedTimeout,
    ILogger<ChaosUpstreamHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var mode = Chaos.Get(httpContextAccessor.HttpContext);

        if (mode.HasFlag(ChaosMode.UpstreamCircuitOpen))
        {
            logger.LogWarning("Chaos: simulating open circuit breaker for {Host}", request.RequestUri?.Host);
            throw new BrokenCircuitException("Simulated open circuit (X-Chaos: upstream-circuit-open).");
        }

        if (mode.HasFlag(ChaosMode.UpstreamDown))
        {
            logger.LogWarning("Chaos: simulating 503 from {Host}", request.RequestUri?.Host);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        }

        if (mode.HasFlag(ChaosMode.UpstreamTimeout))
        {
            logger.LogWarning("Chaos: simulating no response from {Host} for {Timeout}", request.RequestUri?.Host, simulatedTimeout);
            await Task.Delay(simulatedTimeout, cancellationToken);
            throw new TimeoutRejectedException("Simulated timeout (X-Chaos: upstream-timeout).", simulatedTimeout);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

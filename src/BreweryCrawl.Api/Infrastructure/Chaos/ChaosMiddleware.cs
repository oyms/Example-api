using BreweryCrawl.Api.Infrastructure.Validation;

namespace BreweryCrawl.Api.Infrastructure.Chaos;

/// <summary>
/// Reads the requested chaos modes for /api requests and applies the ones that affect the request itself.
/// Upstream modes are applied by <see cref="ChaosUpstreamHandler"/>.
/// </summary>
public sealed class ChaosMiddleware(RequestDelegate next, IConfiguration configuration, TimeProvider timeProvider, ILogger<ChaosMiddleware> logger)
{
    private readonly TimeSpan _slowDelay = configuration.GetValue("Chaos:SlowDelay", TimeSpan.FromSeconds(5));

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var raw = string.Join(',', [.. context.Request.Headers[Chaos.HeaderName], .. context.Request.Query[Chaos.QueryName]]);
        if (string.IsNullOrWhiteSpace(raw))
        {
            await next(context);
            return;
        }

        var mode = ChaosMode.None;
        var errors = new ValidationErrors(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        foreach (var value in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Chaos.Modes.TryGetValue(value, out var parsed))
            {
                mode |= parsed;
            }
            else
            {
                errors.OneOf(Chaos.HeaderName, value, [.. Chaos.Modes.Keys]);
            }
        }

        if (errors.HasErrors)
        {
            await errors.ToProblem().ExecuteAsync(context);
            return;
        }

        logger.LogWarning("Chaos requested: {ChaosMode}", mode);
        Chaos.Set(context, mode);

        if (mode.HasFlag(ChaosMode.Slow))
        {
            logger.LogInformation("Chaos: delaying request by {Delay}", _slowDelay);
            await Task.Delay(_slowDelay, context.RequestAborted);
        }

        if (mode.HasFlag(ChaosMode.Fail))
        {
            throw new ChaosException("Simulated failure (X-Chaos: fail).");
        }

        if (mode.HasFlag(ChaosMode.Flaky) && Random.Shared.Next(2) == 0)
        {
            throw new ChaosException("Simulated flaky failure (X-Chaos: flaky).");
        }

        await next(context);
    }
}

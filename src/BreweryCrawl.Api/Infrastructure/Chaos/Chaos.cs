namespace BreweryCrawl.Api.Infrastructure.Chaos;

/// <summary>
/// Faults that can be triggered per request with the <c>X-Chaos</c> header or the <c>chaos</c> query parameter,
/// e.g. <c>X-Chaos: upstream-down,no-fallback</c>. Lets workshop participants trigger every error on demand.
/// </summary>
[Flags]
public enum ChaosMode
{
    None = 0,

    /// <summary>The endpoint throws an unexpected exception → 500.</summary>
    Fail = 1 << 0,

    /// <summary>The request takes several seconds. Useful for loading states and cancellation.</summary>
    Slow = 1 << 1,

    /// <summary>Fails with a 500 about half of the time. Useful for retry logic.</summary>
    Flaky = 1 << 2,

    /// <summary>Open Brewery DB answers with 503 → retries, then fallback or 502.</summary>
    UpstreamDown = 1 << 3,

    /// <summary>Open Brewery DB never answers → timeouts, then fallback or 504.</summary>
    UpstreamTimeout = 1 << 4,

    /// <summary>Behaves as if the circuit breaker towards Open Brewery DB is open → fallback or 503.</summary>
    UpstreamCircuitOpen = 1 << 5,

    /// <summary>Disables the snapshot fallback for this request, so upstream failures reach the client.</summary>
    NoFallback = 1 << 6,
}

public static class Chaos
{
    public const string HeaderName = "X-Chaos";
    public const string QueryName = "chaos";

    public static readonly IReadOnlyDictionary<string, ChaosMode> Modes = new Dictionary<string, ChaosMode>(StringComparer.OrdinalIgnoreCase)
    {
        ["fail"] = ChaosMode.Fail,
        ["slow"] = ChaosMode.Slow,
        ["flaky"] = ChaosMode.Flaky,
        ["upstream-down"] = ChaosMode.UpstreamDown,
        ["upstream-timeout"] = ChaosMode.UpstreamTimeout,
        ["upstream-circuit-open"] = ChaosMode.UpstreamCircuitOpen,
        ["no-fallback"] = ChaosMode.NoFallback,
    };

    private static readonly object ItemsKey = new();

    public static ChaosMode Get(HttpContext? httpContext) =>
        httpContext?.Items.TryGetValue(ItemsKey, out var value) == true ? (ChaosMode)value! : ChaosMode.None;

    internal static void Set(HttpContext httpContext, ChaosMode mode) => httpContext.Items[ItemsKey] = mode;

    public static bool AffectsUpstream(this ChaosMode mode) =>
        (mode & (ChaosMode.UpstreamDown | ChaosMode.UpstreamTimeout | ChaosMode.UpstreamCircuitOpen)) != 0;
}

/// <summary>Thrown on purpose by the chaos middleware.</summary>
public sealed class ChaosException(string message) : Exception(message);

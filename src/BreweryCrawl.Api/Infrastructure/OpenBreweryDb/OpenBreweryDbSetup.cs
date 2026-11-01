using BreweryCrawl.Api.Infrastructure.Chaos;
using Polly;

namespace BreweryCrawl.Api.Infrastructure.OpenBreweryDb;

public sealed class OpenBreweryDbOptions
{
    public Uri BaseUrl { get; set; } = new("https://api.openbrewerydb.org/v1/");
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(6);
}

public static class OpenBreweryDbSetup
{
    public static IServiceCollection AddOpenBreweryDb(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("OpenBreweryDb").Get<OpenBreweryDbOptions>() ?? new OpenBreweryDbOptions();

        services.AddSingleton<BrewerySnapshot>();
        services.AddTransient(sp => ActivatorUtilities.CreateInstance<ChaosUpstreamHandler>(sp, options.TotalTimeout));

        var httpClient = services.AddHttpClient<OpenBreweryDbClient>(client => client.BaseAddress = options.BaseUrl);

        // Added first = runs outermost, so simulated faults never reach the resilience handler below.
        httpClient.AddHttpMessageHandler<ChaosUpstreamHandler>();

        // Retries, timeouts and a circuit breaker. Tuned to fail fast enough to be fun in a workshop.
        httpClient.AddStandardResilienceHandler(resilience =>
        {
            resilience.AttemptTimeout.Timeout = options.AttemptTimeout;
            resilience.TotalRequestTimeout.Timeout = options.TotalTimeout;
            resilience.Retry.MaxRetryAttempts = 2;
            resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);
            resilience.Retry.BackoffType = DelayBackoffType.Exponential;
            resilience.CircuitBreaker.MinimumThroughput = 5;
            resilience.CircuitBreaker.FailureRatio = 0.5;
            resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}

using System.Globalization;
using System.Threading.RateLimiting;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;

namespace BreweryCrawl.Api.Infrastructure.RateLimiting;

public static class RateLimitingSetup
{
    /// <summary>Policy for creating crawls: a few per minute per user. Low on purpose, so 429 is easy to trigger.</summary>
    public const string CreateCrawlPolicy = "create-crawl";

    public static readonly ErrorDefinition RateLimited = new(
        "request.rate_limited", StatusCodes.Status429TooManyRequests, "Too many requests",
        "You have sent too many requests. Wait the number of seconds in the Retry-After header and try again.");

    public static IServiceCollection AddWorkshopRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:CreateCrawl:PermitLimit", 3);
        var window = configuration.GetValue("RateLimiting:CreateCrawl:Window", TimeSpan.FromMinutes(1));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(CreateCrawlPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = window }));

            options.OnRejected = async (context, _) =>
            {
                var http = context.HttpContext;
                var parameters = new Dictionary<string, object?>();
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                    http.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    parameters["retryAfterSeconds"] = seconds;
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("BreweryCrawl.Api.RateLimiting")
                    .LogWarning("Rate limit exceeded for {User}", http.User.Identity?.Name ?? "anonymous");

                await RateLimited
                    .WithDetail($"At most {permitLimit} requests per {window.TotalSeconds:0} seconds are allowed.", parameters)
                    .ToProblem()
                    .ExecuteAsync(http);
            };
        });

        return services;
    }
}

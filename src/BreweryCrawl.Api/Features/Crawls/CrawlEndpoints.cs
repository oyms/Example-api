using System.Security.Claims;
using BreweryCrawl.Api.Infrastructure.Auth;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using BreweryCrawl.Api.Infrastructure.RateLimiting;
using BreweryCrawl.Api.Infrastructure.Validation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace BreweryCrawl.Api.Features.Crawls;

public static class CrawlEndpoints
{
    public static IEndpointRouteBuilder MapCrawlEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/crawls")
            .WithTags("Crawls")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/", ListCrawls)
            .WithSummary("List crawls");

        group.MapGet("/{id:guid}", GetCrawl)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Get a crawl. The ETag header holds its version.");

        // Everything below changes data, so it requires a user (X-User header).
        var changes = group.MapGroup("")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        changes.MapPost("/", CreateCrawl)
            .WithValidation<CreateCrawlRequest>()
            .RequireRateLimiting(RateLimitingSetup.CreateCrawlPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithSummary("Create a crawl");

        changes.MapPut("/{id:guid}", UpdateCrawl)
            .WithValidation<UpdateCrawlRequest>()
            .ProducesChangeProblems()
            .WithSummary("Rename or move a crawl. Send If-Match with the ETag to avoid overwriting someone else's change.");

        changes.MapDelete("/{id:guid}", DeleteCrawl)
            .ProducesChangeProblems()
            .WithSummary("Delete a crawl");

        changes.MapPost("/{id:guid}/stops", AddStop)
            .WithValidation<AddStopRequest>()
            .ProducesChangeProblems()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .WithSummary("Add a brewery as the next stop");

        changes.MapDelete("/{id:guid}/stops/{breweryId}", RemoveStop)
            .ProducesChangeProblems()
            .WithSummary("Remove a stop");

        return app;
    }

    private static RouteHandlerBuilder ProducesChangeProblems(this RouteHandlerBuilder builder) => builder
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status412PreconditionFailed)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Ok<IEnumerable<CrawlResponse>>> ListCrawls(
        string? city, CrawlService crawls, CancellationToken cancellationToken)
    {
        var result = await crawls.ListAsync(city, cancellationToken);
        return TypedResults.Ok(result.Select(CrawlResponse.From));
    }

    private static async Task<Results<Ok<CrawlResponse>, ProblemHttpResult>> GetCrawl(
        Guid id, CrawlService crawls, HttpResponse response, CancellationToken cancellationToken) =>
        (await crawls.GetAsync(id, cancellationToken)).ToOk(response);

    private static async Task<Created<CrawlResponse>> CreateCrawl(
        CreateCrawlRequest request, ClaimsPrincipal user, CrawlService crawls, HttpResponse response,
        CancellationToken cancellationToken)
    {
        var crawl = await crawls.CreateAsync(request, user.UserName(), cancellationToken);
        response.Headers.ETag = CrawlETag.Format(crawl.Version);
        return TypedResults.Created($"/api/crawls/{crawl.Id}", CrawlResponse.From(crawl));
    }

    private static async Task<Results<Ok<CrawlResponse>, ProblemHttpResult>> UpdateCrawl(
        Guid id, UpdateCrawlRequest request, ClaimsPrincipal user, CrawlService crawls,
        HttpRequest httpRequest, HttpResponse response, CancellationToken cancellationToken) =>
        (await crawls.UpdateAsync(id, request, user.UserName(), CrawlETag.ParseIfMatch(httpRequest), cancellationToken))
        .ToOk(response);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteCrawl(
        Guid id, ClaimsPrincipal user, CrawlService crawls, HttpRequest httpRequest, CancellationToken cancellationToken)
    {
        var result = await crawls.DeleteAsync(id, user.UserName(), CrawlETag.ParseIfMatch(httpRequest), cancellationToken);
        return result.Match<Results<NoContent, ProblemHttpResult>>(_ => TypedResults.NoContent(), error => error.ToProblem());
    }

    private static async Task<Results<Ok<CrawlResponse>, ProblemHttpResult>> AddStop(
        Guid id, AddStopRequest request, ClaimsPrincipal user, CrawlService crawls,
        HttpRequest httpRequest, HttpResponse response, CancellationToken cancellationToken) =>
        (await crawls.AddStopAsync(id, request, user.UserName(), CrawlETag.ParseIfMatch(httpRequest), cancellationToken))
        .ToOk(response);

    private static async Task<Results<Ok<CrawlResponse>, ProblemHttpResult>> RemoveStop(
        Guid id, string breweryId, ClaimsPrincipal user, CrawlService crawls,
        HttpRequest httpRequest, HttpResponse response, CancellationToken cancellationToken) =>
        (await crawls.RemoveStopAsync(id, breweryId, user.UserName(), CrawlETag.ParseIfMatch(httpRequest), cancellationToken))
        .ToOk(response);

    /// <summary>200 with the crawl and its ETag, or the error as ProblemDetails.</summary>
    private static Results<Ok<CrawlResponse>, ProblemHttpResult> ToOk(this Result<Crawl> result, HttpResponse response) =>
        result.Match<Results<Ok<CrawlResponse>, ProblemHttpResult>>(
            crawl =>
            {
                response.Headers.ETag = CrawlETag.Format(crawl.Version);
                return TypedResults.Ok(CrawlResponse.From(crawl));
            },
            error => error.ToProblem());
}

/// <summary>
/// The crawl's version as an HTTP ETag. Clients send it back in If-Match when changing the crawl,
/// and get 412 Precondition Failed if someone else changed it in the meantime.
/// </summary>
public static class CrawlETag
{
    public static string Format(int version) => $"\"{version}\"";

    /// <returns>
    /// The versions listed in If-Match, or null if there is no If-Match (or it is "*"), meaning "don't check".
    /// Weak ETags (W/"1") never match: If-Match requires strong comparison.
    /// </returns>
    public static IReadOnlyCollection<int>? ParseIfMatch(HttpRequest request)
    {
        var ifMatch = request.GetTypedHeaders().IfMatch;
        if (ifMatch.Count == 0 || ifMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any)))
        {
            return null;
        }

        return ifMatch
            .Where(tag => !tag.IsWeak)
            .Select(tag => int.TryParse(tag.Tag.Value?.Trim('"'), out var version) ? version : (int?)null)
            .OfType<int>()
            .ToList();
    }
}

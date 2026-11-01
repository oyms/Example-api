using Microsoft.AspNetCore.WebUtilities;

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

/// <summary>Errors that are not tied to a specific feature.</summary>
public static class CommonErrors
{
    public static readonly ErrorDefinition ValidationFailed = new(
        "validation.failed", StatusCodes.Status400BadRequest, "One or more validation errors occurred",
        "The request contains invalid values. See 'errors' and 'errorCodes' for details per field.");

    public static readonly ErrorDefinition MalformedRequest = new(
        "request.malformed", StatusCodes.Status400BadRequest, "Malformed request",
        "The request could not be read, e.g. invalid JSON or a parameter of the wrong type.");

    public static readonly ErrorDefinition RouteNotFound = new(
        "request.not_found", StatusCodes.Status404NotFound, "Not found",
        "No endpoint exists at this URL.");

    public static readonly ErrorDefinition MethodNotAllowed = new(
        "request.method_not_allowed", StatusCodes.Status405MethodNotAllowed, "Method not allowed",
        "The endpoint exists, but does not support this HTTP method.");

    public static readonly ErrorDefinition ConcurrencyConflict = new(
        "request.concurrency_conflict", StatusCodes.Status409Conflict, "Changed by someone else",
        "Someone else changed the same data at the same time. Fetch it again and retry.");

    public static readonly ErrorDefinition Unexpected = new(
        "server.unexpected", StatusCodes.Status500InternalServerError, "An unexpected error occurred",
        "Something went wrong on our side. Use the traceId when reporting the problem.");

    public static readonly ErrorDefinition UpstreamUnavailable = new(
        "upstream.unavailable", StatusCodes.Status502BadGateway, "Upstream service failed",
        "A service we depend on returned an error.");

    public static readonly ErrorDefinition UpstreamCircuitOpen = new(
        "upstream.circuit_open", StatusCodes.Status503ServiceUnavailable, "Upstream service temporarily disabled",
        "A service we depend on has failed repeatedly, so we stopped calling it for a short while. Try again later.");

    public static readonly ErrorDefinition UpstreamTimeout = new(
        "upstream.timeout", StatusCodes.Status504GatewayTimeout, "Upstream service timed out",
        "A service we depend on did not respond in time.");

    public static readonly ErrorDefinition ErrorCodeNotFound = new(
        "error.not_found", StatusCodes.Status404NotFound, "Unknown error code",
        "There is no error with this code. See GET /errors for all codes.");

    /// <summary>
    /// Fallback for status codes the framework produces on its own (e.g. 413 or 415),
    /// so every problem still has an errorCode and a type, e.g. "http.415".
    /// </summary>
    public static ErrorDefinition ForStatus(int status) => new(
        $"http.{status}", status, ReasonPhrases.GetReasonPhrase(status),
        $"HTTP {status} {ReasonPhrases.GetReasonPhrase(status)}, produced by the framework.");

    public static IEnumerable<ErrorDefinition> All =>
    [
        ValidationFailed, MalformedRequest, RouteNotFound, MethodNotAllowed, ConcurrencyConflict,
        Unexpected, UpstreamUnavailable, UpstreamCircuitOpen, UpstreamTimeout, ErrorCodeNotFound,
    ];
}

namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

public enum UpstreamFailure
{
    /// <summary>The upstream service returned an error or an unreadable response.</summary>
    Unavailable,

    /// <summary>The upstream service did not answer in time.</summary>
    Timeout,

    /// <summary>The circuit breaker is open after repeated failures, so the call was never made.</summary>
    CircuitOpen,
}

/// <summary>
/// Thrown when a service we depend on fails. This is unexpected from the caller's point of view,
/// so it is an exception rather than a <see cref="Result{T}"/>. <see cref="GlobalExceptionHandler"/>
/// maps it to 502/503/504.
/// </summary>
public sealed class UpstreamException(string service, UpstreamFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Service { get; } = service;
    public UpstreamFailure Failure { get; } = failure;
}

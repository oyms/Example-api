namespace BreweryCrawl.Api.Infrastructure.ErrorHandling;

/// <summary>
/// A kind of error the API can return. Each definition becomes a ProblemDetails "type"
/// and has a stable <see cref="Code"/> that clients can switch on (e.g. to pick a translated message).
/// </summary>
public sealed record ErrorDefinition(string Code, int Status, string Title, string Description)
{
    public string TypeUri => $"/errors/{Code}";

    /// <summary>Creates a concrete error with a human-readable detail and optional parameters for the client.</summary>
    public Error WithDetail(string detail, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(this, detail, parameters);
}

/// <summary>
/// An expected error, returned as a value (not thrown) from domain and application code.
/// </summary>
public sealed record Error(
    ErrorDefinition Definition,
    string Detail,
    IReadOnlyDictionary<string, object?>? Parameters = null);

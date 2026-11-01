using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BreweryCrawl.Api.Infrastructure.Validation;

/// <summary>Stable codes for field-level validation errors. Clients can use these (and their params) for translated messages.</summary>
public static class ValidationCodes
{
    public const string Required = "validation.required";
    public const string TooLong = "validation.too_long";
    public const string OutOfRange = "validation.out_of_range";
    public const string InvalidValue = "validation.invalid_value";
    public const string InThePast = "validation.in_the_past";

    public static IReadOnlyList<string> All => [Required, TooLong, OutOfRange, InvalidValue, InThePast];
}

public sealed record FieldError(string Code, IReadOnlyDictionary<string, object?>? Params = null);

/// <summary>
/// Collects validation errors per field and turns them into a 400 ValidationProblemDetails with:
/// <list type="bullet">
/// <item><c>errors</c>: field → messages (the standard ASP.NET Core format)</item>
/// <item><c>errorCodes</c>: field → codes with params (for clients that translate messages themselves)</item>
/// </list>
/// </summary>
public sealed class ValidationErrors(DateOnly today)
{
    private readonly Dictionary<string, List<(string Message, FieldError Error)>> _errors = [];

    /// <summary>Today's date (UTC), for rules like "must not be in the past".</summary>
    public DateOnly Today { get; } = today;

    public bool HasErrors => _errors.Count > 0;

    public IEnumerable<string> Fields => _errors.Keys;

    public ValidationErrors Add(string field, string message, string code, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }

        list.Add((message, new FieldError(code, parameters)));
        return this;
    }

    public ValidationErrors Required(string field, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Add(field, $"'{field}' is required.", ValidationCodes.Required)
            : this;

    public ValidationErrors Required<T>(string field, T? value) where T : struct =>
        value is null
            ? Add(field, $"'{field}' is required.", ValidationCodes.Required)
            : this;

    public ValidationErrors NotInPast(string field, DateOnly? value) =>
        value < Today
            ? Add(field, $"'{field}' can't be in the past.", ValidationCodes.InThePast,
                new Dictionary<string, object?> { ["today"] = Today })
            : this;

    public ValidationErrors MaxLength(string field, string? value, int max) =>
        value is not null && value.Length > max
            ? Add(field, $"'{field}' must be at most {max} characters.", ValidationCodes.TooLong,
                new Dictionary<string, object?> { ["max"] = max })
            : this;

    public ValidationErrors Range(string field, int value, int min, int max) =>
        value < min || value > max
            ? Add(field, $"'{field}' must be between {min} and {max}.", ValidationCodes.OutOfRange,
                new Dictionary<string, object?> { ["min"] = min, ["max"] = max })
            : this;

    public ValidationErrors OneOf(string field, string? value, IReadOnlyCollection<string> allowed) =>
        value is not null && !allowed.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? Add(field, $"'{value}' is not a valid value for '{field}'. Allowed values: {string.Join(", ", allowed)}.",
                ValidationCodes.InvalidValue, new Dictionary<string, object?> { ["allowed"] = allowed })
            : this;

    public ProblemHttpResult ToProblem()
    {
        var problem = new HttpValidationProblemDetails(
            _errors.ToDictionary(e => e.Key, e => e.Value.Select(v => v.Message).ToArray()))
        {
            Type = CommonErrors.ValidationFailed.TypeUri,
            Title = CommonErrors.ValidationFailed.Title,
            Status = CommonErrors.ValidationFailed.Status,
            Detail = "See 'errors' for messages and 'errorCodes' for machine-readable codes per field.",
            Extensions =
            {
                ["errorCode"] = CommonErrors.ValidationFailed.Code,
                ["errorCodes"] = _errors.ToDictionary(e => e.Key, e => e.Value.Select(v => v.Error).ToArray()),
            },
        };

        return TypedResults.Problem(problem);
    }
}

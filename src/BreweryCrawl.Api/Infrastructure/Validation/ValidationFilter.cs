namespace BreweryCrawl.Api.Infrastructure.Validation;

/// <summary>Implemented by request types (bodies and <c>[AsParameters]</c> query objects) that can validate themselves.</summary>
public interface IValidatable
{
    void Validate(ValidationErrors errors);
}

/// <summary>Validates the endpoint argument of type <typeparamref name="T"/> before the handler runs.</summary>
public sealed class ValidationFilter<T>(ILogger<ValidationFilter<T>> logger) : IEndpointFilter where T : IValidatable
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is { } argument)
        {
            var errors = new ValidationErrors();
            argument.Validate(errors);
            if (errors.HasErrors)
            {
                logger.LogInformation("Validation failed for {RequestType}, invalid fields: {Fields}",
                    typeof(T).Name, errors.Fields);
                return errors.ToProblem();
            }
        }

        return await next(context);
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) where T : IValidatable =>
        builder
            .AddEndpointFilter<ValidationFilter<T>>()
            .ProducesValidationProblem();
}

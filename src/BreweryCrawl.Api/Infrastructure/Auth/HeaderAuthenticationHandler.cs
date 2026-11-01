using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BreweryCrawl.Api.Infrastructure.Auth;

/// <summary>
/// Fake authentication for the workshop: the user is whoever the <c>X-User</c> header says. Never do this for real!
/// It still plugs into ASP.NET Core authentication like a real scheme (JWT, cookies), so endpoints use
/// <c>RequireAuthorization()</c> and a missing user gives a proper 401.
/// </summary>
public sealed partial class HeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IProblemDetailsService problemDetailsService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "XUser";
    public const string HeaderName = "X-User";

    public static readonly ErrorDefinition Unauthenticated = new(
        "auth.unauthenticated", StatusCodes.Status401Unauthorized, "Not signed in",
        $"This endpoint requires a user. Send the {HeaderName} header, e.g. '{HeaderName}: alice'.");

    [GeneratedRegex("^[a-zA-Z0-9_-]{1,50}$")]
    private static partial Regex ValidUserName();

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userName = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(userName))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!ValidUserName().IsMatch(userName))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                $"The {HeaderName} header must be 1-50 letters, digits, '-' or '_'."));
        }

        // Lower case, so "Alice" and "alice" are the same user.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, userName.ToLowerInvariant())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    /// <summary>Called when an endpoint requires a user and there is none: respond 401 with ProblemDetails.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        var detail = result.Failure?.Message ?? Unauthenticated.Description;

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = HeaderName;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = ErrorResults.CreateProblemDetails(Unauthenticated, detail),
        });
    }
}

public static class AuthSetup
{
    public static IServiceCollection AddFakeAuth(this IServiceCollection services)
    {
        services
            .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);
        services.AddAuthorization();
        return services;
    }

    /// <summary>The signed-in user's name. Only call this on endpoints that require authorization.</summary>
    public static string UserName(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new InvalidOperationException("No authenticated user.");
}

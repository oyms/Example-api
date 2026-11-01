using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Features.Crawls;
using BreweryCrawl.Api.Infrastructure.Auth;
using BreweryCrawl.Api.Infrastructure.Chaos;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using BreweryCrawl.Api.Infrastructure.Logging;
using BreweryCrawl.Api.Infrastructure.OpenBreweryDb;
using BreweryCrawl.Api.Infrastructure.Persistence;
using BreweryCrawl.Api.Infrastructure.RateLimiting;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddLogging();
builder.Services.AddErrorHandling();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFakeAuth();
builder.Services.AddWorkshopRateLimiting(builder.Configuration);

// Without exposed headers, the browser hides them from JavaScript even though they are in the response.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(BreweryEndpoints.DataSourceHeader, "Retry-After", "ETag", "Location")));

builder.Services.AddOpenBreweryDb(builder.Configuration);
builder.Services.Configure<BreweryOptions>(builder.Configuration.GetSection("Breweries"));
builder.Services.AddScoped<BreweryCatalog>();

builder.Services.AddPersistence(builder.Configuration, builder.Environment);
builder.Services.AddScoped<CrawlService>();

var app = builder.Build();

app.Services.GetRequiredService<BrewerySnapshot>();
await app.InitializeDatabaseAsync();

// Order matters:
// - Request logging is outermost, so it sees the final status code after error handling.
// - Authentication runs before rate limiting, so limits are per user.
// - CORS comes before the exception handler, so error responses also get CORS headers.
//   Without them the browser reports a network error instead of the ProblemDetails.
app.UseRequestLogging();
app.UseCors();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<ChaosMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapHealthChecks("/health");

app.MapErrorCatalog();
app.MapBreweryEndpoints();
app.MapCrawlEndpoints();
app.MapAdminEndpoints();

app.Run();

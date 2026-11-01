using BreweryCrawl.Api.Features.Breweries;
using BreweryCrawl.Api.Infrastructure.Chaos;
using BreweryCrawl.Api.Infrastructure.ErrorHandling;
using BreweryCrawl.Api.Infrastructure.Logging;
using BreweryCrawl.Api.Infrastructure.OpenBreweryDb;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddLogging();
builder.Services.AddErrorHandling();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// Without exposed headers, the browser hides them from JavaScript even though they are in the response.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(BreweryEndpoints.DataSourceHeader, "Retry-After", "ETag", "Location")));

builder.Services.AddOpenBreweryDb(builder.Configuration);
builder.Services.Configure<BreweryOptions>(builder.Configuration.GetSection("Breweries"));
builder.Services.AddScoped<BreweryCatalog>();

var app = builder.Build();

app.Services.GetRequiredService<BrewerySnapshot>();

// Order matters:
// - Request logging is outermost, so it sees the final status code after error handling.
// - CORS comes before the exception handler, so error responses also get CORS headers.
//   Without them the browser reports a network error instead of the ProblemDetails.
app.UseRequestLogging();
app.UseCors();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<ChaosMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapHealthChecks("/health");

app.MapErrorCatalog();
app.MapBreweryEndpoints();

app.Run();

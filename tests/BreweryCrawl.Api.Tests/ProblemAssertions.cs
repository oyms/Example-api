using System.Net;
using System.Text.Json;

namespace BreweryCrawl.Api.Tests;

public static class ProblemAssertions
{
    /// <summary>Asserts the response is RFC 9457 ProblemDetails following our contract, and returns the body.</summary>
    public static async Task<JsonElement> AssertProblem(this HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, problem.GetProperty("errorCode").GetString());
        Assert.Equal($"/errors/{errorCode}", problem.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
        return problem;
    }
}

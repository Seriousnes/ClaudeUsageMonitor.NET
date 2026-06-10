using System.Net;
using System.Net.Http.Headers;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class HttpUsageApiClientTests
{
    private sealed class CapturingHandler(string body, HttpStatusCode status, TimeSpan? retryAfter = null)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Captured = request;
            var resp = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (retryAfter is { } ra) resp.Headers.RetryAfter = new RetryConditionHeaderValue(ra);
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task Sends_required_headers_and_returns_body()
    {
        var handler = new CapturingHandler("""{"five_hour":{}}""", HttpStatusCode.OK);
        var client = new HttpUsageApiClient(new HttpClient(handler));

        var body = await client.GetUsageJsonAsync("tok-789");

        Assert.Equal("""{"five_hour":{}}""", body);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", handler.Captured!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Captured.Headers.Authorization!.Scheme);
        Assert.Equal("tok-789", handler.Captured.Headers.Authorization.Parameter);
        Assert.True(handler.Captured.Headers.TryGetValues("anthropic-beta", out var beta));
        Assert.Equal("oauth-2025-04-20", beta!.Single());
    }

    [Fact]
    public async Task Non_success_status_throws_with_status_and_no_retry_after()
    {
        var handler = new CapturingHandler("nope", HttpStatusCode.Unauthorized);
        var client = new HttpUsageApiClient(new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<UsageApiException>(() => client.GetUsageJsonAsync("tok"));
        Assert.Equal(401, ex.StatusCode);
        Assert.Null(ex.RetryAfter);
    }

    [Fact]
    public async Task Rate_limited_throws_with_retry_after_delta()
    {
        var handler = new CapturingHandler("slow down", HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(120));
        var client = new HttpUsageApiClient(new HttpClient(handler));
        var ex = await Assert.ThrowsAsync<UsageApiException>(() => client.GetUsageJsonAsync("tok"));
        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(120), ex.RetryAfter);
    }
}

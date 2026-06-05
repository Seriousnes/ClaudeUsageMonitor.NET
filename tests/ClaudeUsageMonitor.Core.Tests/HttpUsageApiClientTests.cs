using System.Net;
using ClaudeUsageMonitor.Core;

namespace ClaudeUsageMonitor.Core.Tests;

public class HttpUsageApiClientTests
{
    private sealed class CapturingHandler(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Captured = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
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
    public async Task Non_success_status_throws()
    {
        var handler = new CapturingHandler("nope", HttpStatusCode.Unauthorized);
        var client = new HttpUsageApiClient(new HttpClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUsageJsonAsync("tok"));
    }
}

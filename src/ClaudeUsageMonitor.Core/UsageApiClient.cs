using System.Net.Http.Headers;

namespace ClaudeUsageMonitor.Core;

public interface IUsageApiClient
{
    Task<string> GetUsageJsonAsync(string accessToken, CancellationToken ct = default);
}

public class HttpUsageApiClient(HttpClient http) : IUsageApiClient
{
    public const string Url = "https://api.anthropic.com/api/oauth/usage";

    public async Task<string> GetUsageJsonAsync(string accessToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }
}

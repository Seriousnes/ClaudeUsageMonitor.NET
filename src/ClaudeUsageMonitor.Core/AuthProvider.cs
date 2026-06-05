namespace ClaudeUsageMonitor.Core;

public record AuthResult(string? AccessToken, string PlanLabel)
{
    public bool IsValid => AccessToken is not null;
}

public interface ITokenRefresher
{
    Task<string?> RefreshAsync(string refreshToken, CancellationToken ct);
}

/// <summary>v1: the OAuth refresh endpoint is not yet known (spec §7 open detail). Always fails → Stale fallback.</summary>
public sealed class StubTokenRefresher : ITokenRefresher
{
    public Task<string?> RefreshAsync(string refreshToken, CancellationToken ct) => Task.FromResult<string?>(null);
}

public class AuthProvider
{
    private readonly string _credentialsPath;
    private readonly ITokenRefresher _refresher;
    private readonly TimeProvider _time;

    public AuthProvider(string credentialsPath, ITokenRefresher refresher, TimeProvider time)
        => (_credentialsPath, _refresher, _time) = (credentialsPath, refresher, time);

    public async Task<AuthResult> GetTokenAsync(CancellationToken ct = default)
    {
        Credentials creds;
        try { creds = CredentialsReader.Read(_credentialsPath); }
        catch { return new AuthResult(null, "Claude"); }

        var label = PlanLabelMapper.Map(creds.RateLimitTier, creds.SubscriptionType);
        if (creds.ExpiresAt > _time.GetUtcNow())
            return new AuthResult(creds.AccessToken, label);

        // Refreshed token is held in memory only — never written back to .credentials.json.
        var refreshed = await _refresher.RefreshAsync(creds.RefreshToken, ct);
        return new AuthResult(refreshed, label);
    }
}

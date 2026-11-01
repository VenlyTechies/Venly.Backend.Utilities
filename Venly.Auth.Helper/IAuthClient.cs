namespace Venly.Auth.Helper;

/// <summary>
/// The typed client for AuthService's two verified-assertion grants. Credentials never cross this seam: the
/// calling service has already authenticated the subject and this asks AuthService to mint a token from a
/// signed claim that it did.
/// </summary>
public interface IAuthClient
{
    Task<TokenPair?> IssueStaffTokensAsync(
        string staffAccountId, IReadOnlyList<string> roles, CancellationToken ct);

    Task<TokenPair?> IssueCustomerTokensAsync(
        string customerId, string verificationTier, string deviceId, string riskLevel, CancellationToken ct);

    /// <summary>
    /// Redeems a refresh token (the standard <c>refresh_token</c> grant) for a new pair. AuthService rotates the
    /// refresh token on every use and revokes the whole chain when a redeemed one is replayed, so callers must
    /// redeem each refresh token once and keep the new one. Null when AuthService refuses it (expired, revoked,
    /// reused). The default (for implementations that predate refresh) refuses everything.
    /// </summary>
    Task<TokenPair?> RefreshTokensAsync(string refreshToken, CancellationToken ct) =>
        Task.FromResult<TokenPair?>(null);
}

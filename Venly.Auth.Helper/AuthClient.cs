using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Venly.Backend.Common.Hmac;

namespace Venly.Auth.Helper;

public class AuthClient(HttpClient http, IOptions<AuthClientOptions> options) : IAuthClient
{
    private const string TokenPath = "/connect/token";
    private const string StaffGrant = "urn:sendgram:grant-type:verified-staff-assertion";
    private const string CustomerGrant = "urn:sendgram:grant-type:verified-customer-assertion";

    public Task<TokenPair?> IssueStaffTokensAsync(
        string staffAccountId, IReadOnlyList<string> roles, CancellationToken ct) =>
        PostAsync(
            $"grant_type={Uri.EscapeDataString(StaffGrant)}"
            + $"&admin_user_id={Uri.EscapeDataString(staffAccountId)}"
            + $"&roles={Uri.EscapeDataString(string.Join(',', roles))}",
            ct);

    public Task<TokenPair?> IssueCustomerTokensAsync(
        string customerId, string verificationTier, string deviceId, string riskLevel, CancellationToken ct) =>
        PostAsync(
            $"grant_type={Uri.EscapeDataString(CustomerGrant)}"
            + $"&customer_id={Uri.EscapeDataString(customerId)}"
            + $"&verification_tier={Uri.EscapeDataString(verificationTier)}"
            + $"&device_id={Uri.EscapeDataString(deviceId)}"
            + $"&risk_level={Uri.EscapeDataString(riskLevel)}",
            ct);

    /// <summary>
    /// The standard refresh grant. Not HMAC-signed: AuthService treats the refresh token itself as the credential
    /// for this grant (and needs no client_id), so there is no assertion to sign.
    /// </summary>
    public Task<TokenPair?> RefreshTokensAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Task.FromResult<TokenPair?>(null);
        return PostAsync(
            $"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refreshToken)}", ct, sign: false);
    }

    private async Task<TokenPair?> PostAsync(string body, CancellationToken ct, bool sign = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };

        if (sign)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // A unique nonce per request. Without it a captured signed assertion is replayable for the whole
            // timestamp tolerance window and each replay mints a fresh token pair — the leg AdminService's own
            // gateway contract closed with RedisHmacNonceStore, previously left open here.
            var nonce = Guid.NewGuid().ToString("N");

            request.Headers.Add("X-Timestamp", timestamp.ToString());
            request.Headers.Add("X-Nonce", nonce);
            request.Headers.Add(
                "X-Signature",
                HmacSignature.Compute(options.Value.HmacSecret, timestamp, "POST", TokenPath, body, nonce));
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(ct);

        // A 200 carrying an error envelope, or no body at all, is a refusal — the same generic failure a 401
        // is. Only the JSON is tolerated here: a transport fault from SendAsync above is a genuine outage and
        // must keep propagating, so nothing wider than JsonException is caught.
        try
        {
            using var payload = JsonDocument.Parse(content);
            var root = payload.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("access_token", out var accessToken)
                || accessToken.ValueKind != JsonValueKind.String)
                return null;

            return new TokenPair(
                accessToken.GetString() ?? string.Empty,
                root.TryGetProperty("refresh_token", out var refresh) && refresh.ValueKind == JsonValueKind.String
                    ? refresh.GetString() ?? string.Empty
                    : string.Empty,
                // ValueKind is checked before TryGetInt32, which throws rather than returning false when the
                // element is not a number at all.
                root.TryGetProperty("expires_in", out var expires)
                && expires.ValueKind == JsonValueKind.Number
                && expires.TryGetInt32(out var expiresIn)
                    ? expiresIn
                    : 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

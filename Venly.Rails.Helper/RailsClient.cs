using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Venly.Rails.Helper;

/// <summary>
/// Signs and sends the reads onto PaymentService's rails surface.
///
/// <para><see cref="ComputeSignature"/> is the same string every other helper signs — copied rather than
/// reimplemented, because it is what <c>HmacAuthorizationFilter</c> verifies and a version differing by a
/// newline would fail every request with a 401 that reads as a wrong secret. A test asserts it agrees with the
/// audit client's.</para>
///
/// <para>THROWS on any non-2xx, unlike the intent client. Every caller here is a scheduled activity: a failure
/// must reach Temporal so the activity retries, and a reconciliation that "succeeded" having fetched no balances
/// would report Incomplete on a green schedule while the break it should have raised went unnoticed.</para>
/// </summary>
public sealed class RailsClient(HttpClient httpClient, IOptions<RailsClientOptions> options)
    : IRailsClient, IRatesMaintenanceClient
{
    public const string BalancesPath = "/internal/payment/rails/balances";
    public const string StatementPath = "/internal/payment/rails/statement";
    public const string BanksPath = "/internal/payment/rails/banks";
    public const string ResolveAccountPath = "/internal/payment/rails/resolve-account";

    public const string QuotesPath = "/internal/payment/rails/quotes";
    public const string PayoutsPath = "/internal/payment/rails/payouts";
    public const string ConversionsPath = "/internal/payment/rails/conversions";
    public const string CheckoutPath = "/internal/payment/rails/checkout";

    /// <summary>By OUR reference, which is what the provider echoed back as customerReference.</summary>
    public static string RequeryPath(string ourReference) =>
        $"{PayoutsPath}/{Uri.EscapeDataString(ourReference)}/requery";

    /// <summary>Not under /rails: the rate capture is its own surface, reached through
    /// <see cref="IRatesMaintenanceClient"/> rather than the read-only rails interface.</summary>
    public const string SnapshotRatesPath = "/internal/payment/rates/snapshot";

    public const string EvaluateRateAlertsPath = "/internal/payment/rates/alerts/evaluate";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<RateSnapshotSummaryResult> SnapshotRatesAsync(CancellationToken ct = default) =>
        SendAsync<RateSnapshotSummaryResult>(HttpMethod.Post, SnapshotRatesPath, null, ct);

    public Task<RateAlertRunSummaryResult> EvaluateRateAlertsAsync(CancellationToken ct = default) =>
        SendAsync<RateAlertRunSummaryResult>(HttpMethod.Post, EvaluateRateAlertsPath, null, ct);

    public Task<RailsQuoteResult> GenerateQuoteAsync(
        RailsQuoteRequestBody body, CancellationToken ct = default) =>
        SendAsync<RailsQuoteResult>(HttpMethod.Post, QuotesPath, body, ct);

    public Task<RailsPayoutAckResult> InitiatePayoutAsync(
        RailsPayoutRequestBody body, CancellationToken ct = default) =>
        SendAsync<RailsPayoutAckResult>(HttpMethod.Post, PayoutsPath, body, ct);

    public Task<RailsPayoutAckResult> RequeryPayoutAsync(
        string ourReference, CancellationToken ct = default) =>
        SendAsync<RailsPayoutAckResult>(HttpMethod.Post, RequeryPath(ourReference), null, ct);

    public Task<RailsConversionResult> InitiateConversionAsync(
        RailsConversionRequestBody body, CancellationToken ct = default) =>
        SendAsync<RailsConversionResult>(HttpMethod.Post, ConversionsPath, body, ct);

    public Task<RailsCheckoutResult> CreateCheckoutAsync(
        RailsCheckoutRequestBody body, CancellationToken ct = default) =>
        SendAsync<RailsCheckoutResult>(HttpMethod.Post, CheckoutPath, body, ct);

    public Task<RailsBalancesResult> GetBalancesAsync(CancellationToken ct = default) =>
        SendAsync<RailsBalancesResult>(HttpMethod.Get, BalancesPath, null, ct);

    public Task<List<RailsStatementLineResult>> GetStatementAsync(
        string currency, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        SendAsync<List<RailsStatementLineResult>>(
            HttpMethod.Get,
            $"{StatementPath}?currency={currency}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}",
            null,
            ct);

    public Task<List<RailsBankResult>> GetBanksAsync(
        string currency, string country, CancellationToken ct = default) =>
        SendAsync<List<RailsBankResult>>(
            HttpMethod.Get, $"{BanksPath}?currency={currency}&country={country}", null, ct);

    public Task<RailsAccountNameResult> ResolveAccountAsync(
        string accountNumber, string bankCode, CancellationToken ct = default) =>
        SendAsync<RailsAccountNameResult>(
            HttpMethod.Post,
            ResolveAccountPath,
            new ResolveAccountRequestBody(accountNumber, bankCode),
            ct);

    private async Task<T> SendAsync<T>(
        HttpMethod method, string path, object? payload, CancellationToken ct)
        where T : class
    {
        var secret = options.Value.HmacSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"{RailsClientOptions.SectionName}:HmacSecret is not configured. Without it this client signs "
                + "with an empty key and every call comes back 401 -- which reads as a wrong secret rather "
                + "than a missing one.");
        }

        // The signature covers the PATH AND QUERY exactly as sent. Signing the path alone would fail every
        // filtered read, because the filter hashes what it received.
        var body = payload is null ? string.Empty : JsonSerializer.Serialize(payload, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(secret, timestamp, method.Method, path, body);

        using var request = new HttpRequestMessage(method, path);

        if (payload is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Signature", signature);

        using var response = await httpClient.SendAsync(request, ct);

        // Throws on any non-2xx, so the activity fails and Temporal retries. See the class remarks.
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        var envelope = JsonSerializer.Deserialize<Envelope<T>>(raw, Json);

        return envelope?.ResponseData
               ?? throw new InvalidOperationException(
                   $"PaymentService answered {(int)response.StatusCode} for {method} {path} with no "
                   + "responseData. Treating this as a failure so Temporal retries rather than reconciling "
                   + "against balances that were never fetched.");
    }

    /// <summary>
    /// Identical to every other helper's by design. Exposed so a test can recompute what the client sent.
    /// </summary>
    public static string ComputeSignature(
        string secret, long timestamp, string method, string path, string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var signingString = $"{timestamp}\n{method}\n{path}\n{bodyHash}";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signingString));
        return Convert.ToBase64String(hash);
    }

    private sealed record Envelope<T>(T? ResponseData) where T : class;
}

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Venly.Wallet.Helper;

/// <summary>
/// Signs and posts WalletService's scheduled maintenance calls.
///
/// <para>The signing string is copied from <c>AuditMaintenanceClient.ComputeSignature</c> rather than
/// reimplemented, and that is the point: it is the string <c>HmacAuthorizationFilter</c> verifies, and a second
/// implementation differing by a newline would fail every request with a 401 that reads as a wrong secret.</para>
/// </summary>
public sealed class WalletMaintenanceClient(
    HttpClient httpClient, IOptions<WalletClientOptions> options) : IWalletMaintenanceClient
{
    public const string ExpirePath = "/internal/wallet/intents/expire";
    public const string ReconciliationRunPath = "/internal/wallet/reconciliation/runs";
    public const string ProviderBalancesPath = "/internal/wallet/reconciliation/provider-balances";
    public const string SafeguardingPath = "/internal/wallet/reconciliation/safeguarding-snapshots";
    public const string FxPositionPath = "/internal/wallet/fx/position-snapshots";

    /// <summary>The rate series moved here with the feature that reads it. See IWalletMaintenanceClient.</summary>
    public const string StoreRateSnapshotPath = "/internal/wallet/rates/snapshot";

    public const string EvaluateRateAlertsPath = "/internal/wallet/rates/alerts/evaluate";

    /// <summary>
    /// Web defaults, matching the camelCase JSON the services exchange. WalletService's envelope is serialised
    /// by ASP.NET's defaults, so a reader that used the framework defaults instead would find
    /// <c>responseData</c> null on every call.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ExpireIntentsResult> ExpireReservationsAsync(
        int batchSize, CancellationToken ct = default) =>
        PostAsync<ExpireIntentsResult>(ExpirePath, new { batchSize }, ct);

    public Task<List<ReconciliationRunSummary>> RunReconciliationAsync(
        string? currency, CancellationToken ct = default) =>
        PostAsync<List<ReconciliationRunSummary>>(ReconciliationRunPath, new { currency }, ct);

    public Task<SampleFxPositionResultBody> SampleFxPositionsAsync(
        SampleFxPositionRequestBody request, CancellationToken ct = default) =>
        PostAsync<SampleFxPositionResultBody>(FxPositionPath, request, ct);

    public Task<IngestProviderBalancesResultBody> IngestProviderBalancesAsync(
        IngestProviderBalancesRequestBody request, CancellationToken ct = default) =>
        PostAsync<IngestProviderBalancesResultBody>(ProviderBalancesPath, request, ct);

    public Task<List<SafeguardingSnapshotSummary>> GenerateSafeguardingSnapshotsAsync(
        CancellationToken ct = default) =>
        PostAsync<List<SafeguardingSnapshotSummary>>(SafeguardingPath, new { }, ct);

    private async Task<T> PostAsync<T>(string path, object payload, CancellationToken ct)
        where T : class
    {
        var secret = options.Value.HmacSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"{WalletClientOptions.SectionName}:HmacSecret is not configured. Without it this client signs "
                + "with an empty key and every call comes back 401 from inside a Temporal activity — which "
                + "reads as \"the job failed\" with nothing pointing at a credential.");
        }

        var body = JsonSerializer.Serialize(payload, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(secret, timestamp, "POST", path, body);

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Signature", signature);

        using var response = await httpClient.SendAsync(request, ct);

        // Throws on any non-2xx, so the ACTIVITY fails and Temporal retries. Swallowing it would let a
        // reconciliation "succeed" having compared nothing, and the schedule would report green while the
        // break it should have raised went unnoticed until someone asked.
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, ct);

        if (envelope?.ResponseData is null)
        {
            throw new InvalidOperationException(
                $"WalletService returned {(int)response.StatusCode} for POST {path} with no responseData. "
                + "Treating this as a failure so Temporal retries rather than recording work that may not "
                + "have happened.");
        }

        return envelope.ResponseData;
    }

    /// <summary>
    /// Exposed so a test can recompute what the client sent. Identical to
    /// <c>AuditMaintenanceClient.ComputeSignature</c> by design — see the class remarks.
    /// </summary>
    public static string ComputeSignature(
        string secret, long timestamp, string method, string path, string body)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var signingString = $"{timestamp}\n{method}\n{path}\n{bodyHash}";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signingString));
        return Convert.ToBase64String(hash);
    }

    public Task<StoreRateSnapshotResultBody> StoreRateSnapshotAsync(
        StoreRateSnapshotBody body, CancellationToken ct = default) =>
        PostAsync<StoreRateSnapshotResultBody>(StoreRateSnapshotPath, body, ct);

    public Task<RateAlertRunResultBody> EvaluateRateAlertsAsync(CancellationToken ct = default) =>
        PostAsync<RateAlertRunResultBody>(EvaluateRateAlertsPath, new { }, ct);

    /// <summary>
    /// Just enough of <c>RequestResponse&lt;T&gt;</c> to read the payload. Declared here rather than taking a
    /// project reference on Venly.Backend.Common for one envelope shape.
    /// </summary>
    private sealed record Envelope<T>(T? ResponseData) where T : class;
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Venly.Wallet.Helper;

/// <summary>
/// Signs and posts the wallet-provisioning call.
///
/// <para>Signing is <see cref="WalletMaintenanceClient.ComputeSignature"/> rather than a second
/// implementation of it — that is the exact string <c>HmacAuthorizationFilter</c> verifies, and a copy
/// differing by a newline would 401 every call in a way that reads as a wrong secret.</para>
/// </summary>
public sealed class WalletProvisioningClient(
    HttpClient httpClient, IOptions<WalletClientOptions> options) : IWalletProvisioningClient
{
    public const string ProvisionPath = "/internal/wallet/wallets";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WalletProvisionResult> ProvisionAsync(
        string customerId, string baseCurrency, CancellationToken ct = default)
    {
        var secret = options.Value.HmacSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            return new WalletProvisionResult(
                WalletProvisionOutcome.Failed, null,
                $"{WalletClientOptions.SectionName}:HmacSecret is not configured, so this client would sign "
                + "with an empty key and every call would come back 401.");
        }

        var body = JsonSerializer.Serialize(new { customerId, baseCurrency }, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = WalletMaintenanceClient.ComputeSignature(
            secret, timestamp, "POST", ProvisionPath, body);

        using var request = new HttpRequestMessage(HttpMethod.Post, ProvisionPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Signature", signature);

        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Returned, not thrown. The caller is in the middle of promoting a verified customer, and an
            // exception here would fail that promotion over a service that will be back shortly.
            return new WalletProvisionResult(WalletProvisionOutcome.Failed, null, ex.Message);
        }

        using (response)
        {
            // A wallet that already exists is what a retry looks like, and a retry must not read as an error.
            if (response.StatusCode == HttpStatusCode.Conflict)
                return new WalletProvisionResult(WalletProvisionOutcome.AlreadyExists, null, null);

            var envelope = await ReadEnvelopeAsync(response, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new WalletProvisionResult(
                    WalletProvisionOutcome.Failed, null,
                    envelope?.ResponseMessage ?? $"WalletService answered {(int)response.StatusCode}.");
            }

            return new WalletProvisionResult(
                WalletProvisionOutcome.Created, envelope?.ResponseData?.WalletId, null);
        }
    }

    private static async Task<Envelope?> ReadEnvelopeAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Envelope>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A body that is not the envelope tells us nothing extra; the status has already decided.
            return null;
        }
    }

    private sealed record Envelope(string? ResponseMessage, ProvisionedWallet? ResponseData);

    private sealed record ProvisionedWallet(string? WalletId);
}

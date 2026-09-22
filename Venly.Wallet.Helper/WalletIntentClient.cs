using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Venly.Wallet.Helper;

/// <summary>
/// Signs and sends the per-movement intent calls.
///
/// <para>Shares <see cref="WalletClientOptions"/> with the maintenance client — one WalletService, one secret,
/// one base URL — but does NOT share <see cref="IWalletMaintenanceClient"/>: see that interface's remarks on why
/// the two capabilities stay apart.</para>
///
/// <para>Unlike the maintenance client this one does NOT throw on a non-2xx. A webhook handler has to tell a 409
/// from a 422 and act differently, and an exception would collapse that into "the call failed" — so the status
/// comes back in <see cref="WalletCallResult"/> and the handler decides.</para>
/// </summary>
public sealed class WalletIntentClient(
    HttpClient httpClient, IOptions<WalletClientOptions> options) : IWalletIntentClient
{
    public const string IntentsPath = "/internal/wallet/intents";
    public const string RealisationsPath = "/internal/wallet/fx/realisations";

    /// <summary>Where a paid top-up is reported, for WalletService to resolve and credit.</summary>
    public const string SettleFundingPath = "/internal/wallet/funding/settle";

    /// <summary>
    /// Not <c>Uri.EscapeDataString</c>. A reference is a path SEGMENT, and <see cref="Uri"/> normalises
    /// <c>%2F</c> back to a slash when it builds the request URI — so escaping a slash here would produce a path
    /// with an extra segment rather than a segment containing a slash, and WalletService would 404.
    ///
    /// <para>References are generated from a 30-symbol alphabet with a fixed prefix, so they contain nothing
    /// that needs escaping. This method exists to keep the path in ONE place, and to make that reasoning
    /// visible where someone would otherwise reach for EscapeDataString.</para>
    /// </summary>
    public static string ByReferencePath(string reference) =>
        $"{IntentsPath}/by-reference/{reference}";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WalletIntentLookup?> FindByReferenceAsync(
        string reference, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(HttpMethod.Get, ByReferencePath(reference), null, ct);

        // 404 is an ANSWER, not a failure: no movement in this system carries that reference.
        if (status == 404) return null;

        return Read<WalletIntentLookup>(body);
    }

    public async Task<WalletCallResult> ConfirmAsync(
        string intentId, string? postedBy, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            HttpMethod.Post, $"{IntentsPath}/{intentId}/confirm", new { postedBy }, ct);

        return new WalletCallResult(status, Message(body));
    }

    public async Task<WalletCallResult> SettleFundingAsync(
        SettleFundingRequest request, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, SettleFundingPath, request, ct);

        return new WalletCallResult(status, Message(body));
    }

    public async Task<WalletCallResult> FailAsync(
        string intentId, string reason, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            HttpMethod.Post, $"{IntentsPath}/{intentId}/fail", new { reason }, ct);

        return new WalletCallResult(status, Message(body));
    }

    public async Task<(WalletCallResult Result, WalletIntentLookup? Intent)> CreateFundingAsync(
        CreateFundingIntentRequest request, CancellationToken ct = default)
    {
        // Funding, same currency both sides. Shaped HERE rather than by the caller so a webhook handler cannot
        // accidentally reserve a payout.
        var payload = new
        {
            kind = "Funding",
            customerId = request.CustomerId,
            currency = request.Currency,
            amount = request.Amount,
            destinationCurrency = request.Currency,
            destinationAmount = request.Amount,
            idempotencyKey = request.IdempotencyKey,
            correlationId = request.CorrelationId,
            narration = request.Narration,
        };

        var (status, body) = await SendAsync(HttpMethod.Post, IntentsPath, payload, ct);

        var result = new WalletCallResult(status, Message(body));

        // The body is read ONCE and then deserialised twice from the string. Reading the HttpContent twice threw
        // ObjectDisposedException("Cannot access a closed Stream") -- found by the test below on 2026-08-27.
        return (result, result.IsSuccess ? Read<WalletIntentLookup>(body) : null);
    }

    public async Task<(WalletCallResult Result, WalletIntentLookup? Intent)> CreatePayoutAsync(
        CreatePayoutIntentRequest request, CancellationToken ct = default)
    {
        // Shaped here rather than by the caller, exactly as funding is, and for the mirror-image reason: a
        // caller that could set `kind` could turn a funding webhook into a payout. The kind is a property of
        // WHICH METHOD was called, which makes it something the type system decides instead of a string.
        var payload = new
        {
            kind = "Payout",
            customerId = request.CustomerId,
            currency = request.Currency,
            amount = request.Amount,
            destinationCurrency = request.DestinationCurrency,
            destinationAmount = request.DestinationAmount,
            quotedRate = request.QuotedRate,
            quoteSource = request.QuoteSource,
            quoteExpiresAt = request.QuoteExpiresAt,
            idempotencyKey = request.IdempotencyKey,
            correlationId = request.CorrelationId,
            narration = request.Narration,
        };

        var (status, body) = await SendAsync(HttpMethod.Post, IntentsPath, payload, ct);
        var result = new WalletCallResult(status, Message(body));

        return (result, result.IsSuccess ? Read<WalletIntentLookup>(body) : null);
    }

    public async Task<(WalletCallResult Result, WalletIntentLookup? Intent)> CreateInternalTransferAsync(
        CreateInternalTransferIntentRequest request, CancellationToken ct = default)
    {
        // Same currency on both sides by construction: a wallet-to-wallet movement inside SendGram has no
        // provider to convert through, so there is no second currency for a caller to name.
        var payload = new
        {
            kind = "InternalTransfer",
            customerId = request.CustomerId,
            destinationCustomerId = request.DestinationCustomerId,
            currency = request.Currency,
            amount = request.Amount,
            destinationCurrency = request.Currency,
            destinationAmount = request.Amount,
            idempotencyKey = request.IdempotencyKey,
            correlationId = request.CorrelationId,
            narration = request.Narration,
        };

        var (status, body) = await SendAsync(HttpMethod.Post, IntentsPath, payload, ct);
        var result = new WalletCallResult(status, Message(body));

        return (result, result.IsSuccess ? Read<WalletIntentLookup>(body) : null);
    }

    public async Task<WalletCallResult> CancelAsync(
        string intentId, string reason, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            HttpMethod.Post, $"{IntentsPath}/{intentId}/cancel", new { reason }, ct);

        return new WalletCallResult(status, Message(body));
    }

    public async Task<WalletCallResult> RecordFxRealisationAsync(
        string intentId, decimal realisedRate, string? providerReference, CancellationToken ct = default)
    {
        var (status, body) = await SendAsync(
            HttpMethod.Post, RealisationsPath, new { intentId, realisedRate, providerReference }, ct);

        return new WalletCallResult(status, Message(body));
    }

    /// <returns>The status code and the response body as a STRING, read exactly once.</returns>
    private async Task<(int Status, string Body)> SendAsync(
        HttpMethod method, string path, object? payload, CancellationToken ct)
    {
        var secret = options.Value.HmacSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"{WalletClientOptions.SectionName}:HmacSecret is not configured. Without it this client signs "
                + "with an empty key and every call comes back 401 -- which reads as a wrong secret rather "
                + "than a missing one.");
        }

        // An empty body for a GET, and that empty string is what the signature covers -- the same string
        // HmacAuthorizationFilter hashes on its side. Signing "null" or omitting the body hash would fail every
        // GET with a 401 that looked like a wrong secret.
        var requestBody = payload is null ? string.Empty : JsonSerializer.Serialize(payload, Json);

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = WalletMaintenanceClient.ComputeSignature(
            secret, timestamp, method.Method, path, requestBody);

        using var request = new HttpRequestMessage(method, path);

        if (payload is not null)
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Signature", signature);

        using var response = await httpClient.SendAsync(request, ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        return ((int)response.StatusCode, body);
    }

    private static T? Read<T>(string body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<Envelope<T>>(body, Json)?.ResponseData;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The envelope's message, or null when the body is not one. A proxy error page must not lose the status the
    /// caller decides on, so this never throws.
    /// </summary>
    private static string? Message(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<Envelope<object>>(body, Json)?.ResponseMessage;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Envelope<T>(int ResponseCode, string? ResponseMessage, T? ResponseData)
        where T : class;
}

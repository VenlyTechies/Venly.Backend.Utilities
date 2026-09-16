using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Venly.Backend.Common;
using Venly.Backend.Common.Hmac;

namespace Venly.Corridor.Helper;

/// <summary>
/// Reads WalletService's corridor snapshot and answers out of memory.
///
/// <para>
/// <b>Registered as a SINGLETON</b> over a named HttpClient, for the reason
/// <c>Venly.FeatureFlag.Helper</c> documents: the cache is the point, and a scoped or transient client would
/// refetch on every lookup. This one sits on the REGISTRATION path, so that would mean a WalletService round
/// trip for every signup.
/// </para>
/// <para>
/// <b>Stale-if-error, with one difference from the flag client.</b> A failed refresh keeps the previous
/// snapshot, same as there. But where a missing flag snapshot resolves every flag to OFF and carries on, a
/// missing corridor snapshot returns NULL and the caller decides — because the two failure modes are not
/// comparable. A flag reading off delays a feature; a corridor list read as empty would refuse every
/// registration, and a corridor list read as "anything goes" would restore the unvalidated free string this
/// exists to remove. Neither is a decision this client should make on the caller's behalf.
/// </para>
/// </summary>
public sealed class CorridorClient(
    HttpClient httpClient,
    IOptions<CorridorClientOptions> options,
    ILogger<CorridorClient> logger) : ICorridorClient
{
    private const string SnapshotPath = "/internal/wallet/corridors";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One in-flight refresh at a time. Without it a cold start under load sends one request per concurrent
    // caller at the moment the downstream is least able to serve them.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CorridorSnapshot? _snapshot;
    private DateTime _fetchedAtUtc = DateTime.MinValue;

    public async Task<CorridorSnapshot?> GetSnapshotAsync(CancellationToken ct = default)
    {
        if (!IsStale())
            return _snapshot;

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check inside the gate: whoever was ahead of us has already refreshed it.
            if (!IsStale())
                return _snapshot;

            var fetched = await FetchAsync(ct);

            // The timestamp moves even on failure, so a hard-down WalletService is retried once per window
            // rather than on every call. _snapshot is left alone — that is the stale-if-error rule, and on a
            // cold start it leaves it null, which is the "we do not know" the interface promises.
            _fetchedAtUtc = DateTime.UtcNow;

            if (fetched is not null)
                _snapshot = fetched;

            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsStale() =>
        DateTime.UtcNow - _fetchedAtUtc >= TimeSpan.FromSeconds(Math.Max(0, options.Value.CacheSeconds));

    private async Task<CorridorSnapshot?> FetchAsync(CancellationToken ct)
    {
        var secret = options.Value.HmacSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Not attempted rather than signed with an empty key: an unsigned request is rejected downstream
            // as a forgery, which would report a configuration mistake as a security event.
            logger.LogError(
                "CorridorClient:HmacSecret is not configured, so no corridor can be validated.");
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Guid.NewGuid().ToString("N");

        // Empty body: it is a GET. The signature covers the exact path the receiver sees.
        var signature = HmacSignature.Compute(secret, timestamp, "GET", SnapshotPath, string.Empty, nonce);

        using var request = new HttpRequestMessage(HttpMethod.Get, SnapshotPath);
        request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp.ToString());
        request.Headers.TryAddWithoutValidation("X-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Signature", signature);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "The corridor snapshot returned {Status}; serving the previous one.",
                    (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var envelope = JsonSerializer.Deserialize<RequestResponse<CorridorSnapshot>>(body, Json);

            if (envelope?.ResponseData is null)
            {
                logger.LogWarning("The corridor snapshot came back with no body; serving the previous one.");
                return null;
            }

            return envelope.ResponseData;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Never rethrows. The caller decides what an unknown corridor list means for its own operation;
            // this class's job is to report that it does not know, not to fail somebody else's request.
            logger.LogWarning(ex, "Could not fetch the corridor snapshot; serving the previous one.");
            return null;
        }
    }
}

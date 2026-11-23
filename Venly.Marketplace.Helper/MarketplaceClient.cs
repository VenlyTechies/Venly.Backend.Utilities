using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Venly.Backend.Common.Hmac;
namespace Venly.Marketplace.Helper;
public sealed class MarketplaceClient(HttpClient http, IOptions<MarketplaceClientOptions> options, ILogger<MarketplaceClient> logger) : IMarketplaceClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Secret => options.Value.HmacSecret;
    public async Task<BookingLookup> GetBookingAsync(string bookingId, CancellationToken ct)
    {
        try
        {
            using var response = await SignedHttp.SendAsync(http, HttpMethod.Get, $"/internal/bookings/{Uri.EscapeDataString(bookingId)}", null, Secret, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new BookingLookup(LookupStatus.NotFound, null);
            if (!response.IsSuccessStatusCode)
                return Unavailable($"GET booking answered {(int)response.StatusCode}");
            var data = Data(await response.Content.ReadAsStringAsync(ct));
            var booking = data?.Deserialize<BookingInfo>(Json);
            return booking is null ? Unavailable("GET booking returned no data") : new BookingLookup(LookupStatus.Found, booking);
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            return Unavailable(ex.Message);
        }
    }
    public async Task<ApplyOutcome> ApplyPaymentAsync(string bookingId, string paymentId, long amountKobo, string kind, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { paymentId, amountKobo, kind }, Json);
            using var response = await SignedHttp.SendAsync(http, HttpMethod.Post, $"/internal/bookings/{Uri.EscapeDataString(bookingId)}/payments", body, Secret, ct);
            var code = (int)response.StatusCode;
            if (code is 200 or 201)
                return ApplyOutcome.Applied;
            // 409: the booking has ended or its hold ran out, so the payment was applied to nothing. 5xx and 401/429 say
            // nothing about the booking, only that the answer is not available yet.
            if (code == 409)
                return ApplyOutcome.BookingEnded;
            if (code is 400 or 404 or 422)
                return ApplyOutcome.Refused;
            logger.LogWarning("MarketplaceService answered {Status} to a payment for a booking.", code);
            return ApplyOutcome.Unavailable;
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            logger.LogWarning(ex, "Applying a payment at MarketplaceService failed.");
            return ApplyOutcome.Unavailable;
        }
    }
    public async Task<VenueInfo?> GetVenueAsync(string venueId, CancellationToken ct)
    {
        try
        {
            using var response = await SignedHttp.SendAsync(http, HttpMethod.Get, $"/internal/venues/{Uri.EscapeDataString(venueId)}", null, Secret, ct);
            return response.IsSuccessStatusCode ? Data(await response.Content.ReadAsStringAsync(ct))?.Deserialize<VenueInfo>(Json) : null;
        }
        catch (Exception ex) when (IsTransient(ex, ct))
        {
            logger.LogWarning(ex, "Looking up a venue at MarketplaceService failed.");
            return null;
        }
    }
    public async Task<IReadOnlyList<MarketplaceEvent>?> ReadEventsAsync(long after, int take, string? prefix, CancellationToken ct)
    {
        try
        {
            var query = $"?after={after}&take={take}" + (string.IsNullOrEmpty(prefix) ? "" : $"&prefix={Uri.EscapeDataString(prefix)}");
            using var response = await SignedHttp.SendAsync(http, HttpMethod.Get, "/internal/events", null, Secret, ct, query);
            if (!response.IsSuccessStatusCode)
                return null;
            var data = Data(await response.Content.ReadAsStringAsync(ct));
            if (data is not { ValueKind: JsonValueKind.Array } array)
                return null;
            var events = new List<MarketplaceEvent>();
            foreach (var e in array.EnumerateArray())
                events.Add(new MarketplaceEvent(
                    e.GetProperty("sequence").GetInt64(), e.GetProperty("eventType").GetString()!, e.GetProperty("aggregateId").GetString()!,
                    e.GetProperty("occurredAt").GetDateTime(), e.GetProperty("payload").Clone()));
            return events;
        }
        catch (Exception ex) when (IsTransient(ex, ct) || ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            logger.LogWarning(ex, "Reading events from MarketplaceService failed.");
            return null;
        }
    }
    private static JsonElement? Data(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("responseData", out var data) && data.ValueKind != JsonValueKind.Null ? data.Clone() : null;
    }
    private BookingLookup Unavailable(string why)
    {
        logger.LogWarning("MarketplaceService could not answer a booking lookup: {Why}", why);
        return new BookingLookup(LookupStatus.Unavailable, null);
    }
    /// <summary>A network failure or a bad body is an outage to retry; the caller's own cancellation is not, and propagates.</summary>
    private static bool IsTransient(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested;
}

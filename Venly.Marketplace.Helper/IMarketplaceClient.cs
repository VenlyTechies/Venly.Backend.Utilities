using System.Text.Json;
namespace Venly.Marketplace.Helper;
/// <summary>
/// The typed client for Eventspaces MarketplaceService's internal surface, used by Payments. Marketplace owns bookings;
/// Payments asks it what is owed and tells it what was paid, and never reads its tables (DBD 8).
/// </summary>
public interface IMarketplaceClient
{
    /// <summary>Null when the booking does not exist OR Marketplace cannot be reached; use <see cref="BookingLookup"/> when the difference matters.</summary>
    Task<BookingLookup> GetBookingAsync(string bookingId, CancellationToken ct);
    Task<ApplyOutcome> ApplyPaymentAsync(string bookingId, string paymentId, long amountKobo, string kind, CancellationToken ct);
    Task<VenueInfo?> GetVenueAsync(string venueId, CancellationToken ct);
    /// <summary>Events after a sequence. Null when Marketplace cannot answer: the caller keeps its place and asks again.</summary>
    Task<IReadOnlyList<MarketplaceEvent>?> ReadEventsAsync(long after, int take, string? prefix, CancellationToken ct);
}
public record BookingInfo(
    string BookingId, string VenueId, string SpaceId, string OrganiserUserId, string EventDate, string State, string Mode,
    long TotalKobo, long FeeKobo, long DepositKobo, long PaidKobo, long SubtotalKobo, long TaxKobo, string? CancelledBy, string? OrganisationId,
    DateTime? HoldExpiresAt = null);
public enum LookupStatus { Found, NotFound, Unavailable }
public record BookingLookup(LookupStatus Status, BookingInfo? Booking);
public record VenueInfo(string VenueId, string OrganisationId, string Name, string Status);
public record MarketplaceEvent(long Sequence, string EventType, string AggregateId, DateTime OccurredAt, JsonElement Payload);
/// <summary>
/// What Marketplace did with a payment. <see cref="BookingEnded"/> is the one Payments must act on: the money was taken
/// and applied to nothing, so it has to be refunded. <see cref="Unavailable"/> means nothing is known, so the caller retries.
/// </summary>
public enum ApplyOutcome { Applied, BookingEnded, Refused, Unavailable }

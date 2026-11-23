using Venly.Backend.Common;
namespace Venly.Identity.Helper;
/// <summary>
/// The check every service makes before acting on a venue's behalf (SRS SEC-2): may THIS person do THIS to THIS venue?
/// One implementation for every service, so an outage, a denial and an allow mean the same thing everywhere.
/// </summary>
public interface IVenueGuard
{
    /// <summary>
    /// Null when <paramref name="userId"/> holds <paramref name="permission"/> for this venue; otherwise the response the
    /// handler returns as-is: 403 for a denial, 503 when IdentityService could not answer. Handlers call this BEFORE reading
    /// the venue, so a caller without access gets the same 403 whether or not the venue exists.
    /// </summary>
    Task<RequestResponse<T>?> RefuseAsync<T>(string userId, string permission, string venueId, CancellationToken ct);
}
public class VenueGuard(IIdentityClient identity) : IVenueGuard
{
    public const string VenueScope = "venue";
    public const string Forbidden = "You are not authorised to perform this action.";
    public const string IdentityUnavailable = "Authorisation could not be determined. Try again.";
    public async Task<RequestResponse<T>?> RefuseAsync<T>(string userId, string permission, string venueId, CancellationToken ct) =>
        await identity.CheckPermissionAsync(userId, permission, VenueScope, venueId, ct) switch
        {
            PermissionDecision.Allowed => null,
            PermissionDecision.Denied => Fail<T>(403, Forbidden),
            // An outage is not a decision: never a 403, which would read as "you may not", and never an allow.
            _ => Fail<T>(503, IdentityUnavailable),
        };
    private static RequestResponse<T> Fail<T>(int code, string message) => new() { ResponseCode = code, ResponseMessage = message };
}

namespace Venly.Identity.Helper;
/// <summary>
/// The typed client for Eventspaces IdentityService's internal surface. Every Eventspaces service that guards an
/// action uses <see cref="CheckPermissionAsync"/> with the caller's id from the validated token and the scope it
/// is about to act on (SRS SEC-2): the gateway proves WHO is calling, the owning service asks whether that person
/// may do THIS to THIS venue, supplier or event.
/// </summary>
public interface IIdentityClient
{
    /// <summary>
    /// <see cref="PermissionDecision.Unavailable"/> when IdentityService cannot be reached or answers anything
    /// unexpected. Callers must treat that as a 503, never as a denial and never as an allow: an outage is not a decision.
    /// </summary>
    Task<PermissionDecision> CheckPermissionAsync(
        string userId, string permission, string scopeType, string? scopeId, CancellationToken ct);
    /// <summary>
    /// Creates the organisation behind a venue or supplier and grants <paramref name="roleCode"/> to the owner for
    /// that one scope. Idempotent on <paramref name="referenceKey"/>, so a retry after a timeout does not make a
    /// second organisation. Null when IdentityService refuses or cannot be reached.
    /// </summary>
    Task<OrganisationCreated?> CreateOrganisationAsync(CreateOrganisationRequest request, CancellationToken ct);
    /// <summary>
    /// What to call each person, by id. Names only: a service that needs a phone number or an email asks for them
    /// through the flow that is entitled to them (a confirmed booking), never from here. People who do not exist are
    /// absent from the result. An empty result when IdentityService cannot answer: a missing name is a cosmetic
    /// failure and must not fail the page it appears on.
    /// </summary>
    Task<IReadOnlyDictionary<string, string?>> LookupNamesAsync(IEnumerable<string> userIds, CancellationToken ct);
    /// <summary>
    /// The address a payment provider needs to start a checkout. Only Payments asks, and only for the person paying;
    /// every other service gets names only (<see cref="LookupNamesAsync"/>). Null when the person has no email or
    /// IdentityService cannot answer: a checkout cannot start without one, and that is a refusal, not a guess.
    /// </summary>
    Task<string?> GetBillingEmailAsync(string userId, CancellationToken ct);
    /// <summary>
    /// The venues a person holds a live role on (owner, manager or viewer) and the role. Null when IdentityService cannot
    /// answer, so a caller can tell "none" from "not known" and fall back to what it knows itself.
    /// </summary>
    Task<IReadOnlyList<VenueRole>?> ListVenueRolesAsync(string userId, CancellationToken ct);
}
public record VenueRole(string VenueId, string Role);
public enum PermissionDecision { Allowed, Denied, Unavailable }
/// <param name="Type">venue, supplier or planner_agency.</param>
/// <param name="ReferenceKey">The caller's own stable key for this organisation, for example "venue:VEN123".</param>
/// <param name="ScopeType">venue, supplier or event. The scope the owner's role is limited to.</param>
public record CreateOrganisationRequest(
    string OwnerUserId, string Type, string Name, string ReferenceKey,
    string RoleCode, string ScopeType, string ScopeId);
public record OrganisationCreated(string OrganisationId);

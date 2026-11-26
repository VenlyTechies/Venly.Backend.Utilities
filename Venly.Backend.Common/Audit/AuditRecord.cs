namespace Venly.Backend.Common.Audit;
/// <summary>
/// The audit contract between the Eventspaces services and PlatformService (DBD 7, audit_log; SRS SEC-6). A service records
/// what it did by writing one outbox event of type <see cref="EventType"/> whose payload is an <see cref="AuditRecord"/>, in
/// the SAME transaction as the change, so the audit trail cannot miss a change or record one that rolled back. Platform pulls
/// every service's feed with the prefix <see cref="Prefix"/> and appends the records to its log.
///
/// Personal data is left out or masked (DBD 7.2): name the thing by its id, and pass an email or phone through
/// <see cref="AuditMask"/> if it must appear at all.
/// </summary>
public static class AuditEvents
{
    public const string EventType = "audit.recorded";
    public const string Prefix = "audit.";
    public const string AggregateType = "audit";
}
public static class AuditActorTypes
{
    /// <summary>A SendGram staff member in the Eventspaces back office.</summary>
    public const string Staff = "staff";
    /// <summary>An Eventspaces person: an organiser, or a venue's owner or team.</summary>
    public const string User = "user";
    /// <summary>A service acting on its own: a timer, a provider callback, a reconciliation.</summary>
    public const string Service = "service";
    /// <summary>From an id's seed: STF is a SendGram staff member, USR an Eventspaces person, nothing a service.</summary>
    public static string For(string? actorId) =>
        string.IsNullOrEmpty(actorId) ? Service : actorId.StartsWith("STF", StringComparison.Ordinal) ? Staff
        : actorId.StartsWith("USR", StringComparison.Ordinal) ? User : Service;
}
/// <param name="Action">entity.verb, past tense, for example <c>venue.approved</c> or <c>payout.hold_released</c>.</param>
/// <param name="ActorId">Null when <paramref name="ActorType"/> is service.</param>
/// <param name="OldValue">What it was, when that matters. Serialised as JSON; no personal data.</param>
/// <param name="NewValue">What it is now.</param>
/// <param name="Reason">The words the actor gave, when the action takes a reason (send back, pause, hold).</param>
public record AuditRecord(
    string Action, string ActorType, string? ActorId, string TargetType, string TargetId,
    object? OldValue = null, object? NewValue = null, string? Reason = null);
public static class AuditMask
{
    /// <summary>"m•••@example.com": enough to tell two invitations apart, not enough to contact anyone.</summary>
    public static string Email(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "•••" : $"{email[0]}•••{email[at..]}";
    }
}

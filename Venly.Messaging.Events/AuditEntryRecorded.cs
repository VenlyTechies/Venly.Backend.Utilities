namespace Venly.Messaging.Events;

/// <summary>
/// One row of ERD-audit's <c>audit_entry</c>, in flight. Field-for-field with the table, minus the two the
/// receiver owns: <c>id</c> (AuditService assigns it) and <c>recorded_at</c> (the receiving clock, which is
/// deliberately not the sender's — the gap between OccurredAt and RecordedAt is the evidence of a delayed or
/// replayed message).
///
/// BeforeValue and AfterValue are pre-serialised JSON strings rather than objects: the column is jsonb, the
/// shape differs per entity type, and a nested object here would make every consumer of this contract depend
/// on every producer's DTOs.
/// </summary>
public sealed record AuditEntryRecorded(
    AuditActorType ActorType,
    string? ActorId,
    IReadOnlyDictionary<string, string?>? EffectivePermissionsSnapshot,
    string ActionType,
    string EntityType,
    string? EntityId,
    string? BeforeValue,
    string? AfterValue,
    string? Rationale,
    string? ChangeRequestId,
    string? EmergencyAccessId,
    string? DeniedPermissionKey,
    string? SourceIpHash,
    string? CorrelationId,
    DateTimeOffset OccurredAt,
    // Optional, appended so every existing producer and consumer is unaffected: the tenant the action was
    // scoped to, and the caller's raw source IP, for multi-tenant products whose audit log is read per tenant
    // and must show the address (Authet TN-ACC-04). SendGram leaves both null and records only the hash.
    string? TenantId = null,
    string? SourceIp = null);

using Venly.Messaging.Events;

namespace Venly.Audit.Helper;

/// <summary>
/// Everything about the caller that an audit record needs, resolved once per request.
///
/// <para><see cref="TenantId"/> and <see cref="SourceIp"/> are for multi-tenant products whose audit log is
/// read per tenant and must show the caller's address (Authet, TN-ACC-04). They are optional and default to
/// null, so a product that does neither (SendGram) is unaffected.</para>
/// </summary>
public readonly record struct AuditActor(
    AuditActorType Type,
    string? Id,
    IReadOnlyDictionary<string, string?>? PermissionsSnapshot,
    string? SourceIpHash,
    string? CorrelationId,
    string? TenantId = null,
    string? SourceIp = null);

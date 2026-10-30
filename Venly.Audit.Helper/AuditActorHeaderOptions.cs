using Venly.Backend.Common.Authentication;
using Venly.Messaging.Events;

namespace Venly.Audit.Helper;

/// <summary>
/// Which gateway-asserted headers <see cref="HttpContextAuditActorAccessor"/> reads, and how a principal-type
/// header value maps to an <see cref="AuditActorType"/>. Bound from <c>AuditPublisher:ActorHeaders</c>.
///
/// <para>Every default is SendGram's existing header contract, so a product that configures nothing behaves
/// exactly as before. A product with its own gateway (Authet) names its own headers here instead of
/// re-implementing the accessor.</para>
///
/// <para>These headers are only trustworthy because the product's gateway scrubs inbound copies and sets them
/// itself on hops it signs. Never point these at headers a caller can send straight to a service.</para>
/// </summary>
public sealed class AuditActorHeaderOptions
{
    public const string SectionName = AuditPublisherOptions.SectionName + ":ActorHeaders";

    public string IdHeader { get; set; } = PrincipalHeaders.Id;

    public string TypeHeader { get; set; } = PrincipalHeaders.Type;

    public string PermissionHeader { get; set; } = PrincipalHeaders.Permission;

    public string SourceIpHashHeader { get; set; } = PrincipalHeaders.SourceIpHash;

    public string CorrelationIdHeader { get; set; } = PrincipalHeaders.CorrelationId;

    /// <summary>The tenant the request is scoped to. Null (the default) = not a multi-tenant product.</summary>
    public string? TenantIdHeader { get; set; }

    /// <summary>The caller's raw source IP. Null (the default) = only the hash is recorded.</summary>
    public string? SourceIpHeader { get; set; }

    /// <summary>
    /// Principal-type header value → actor type (case-insensitive). Empty (the default) means SendGram's
    /// mapping: <c>staff</c> → Staff, <c>customer</c> → Customer. Anything unmapped is Service, with no id.
    /// </summary>
    public Dictionary<string, AuditActorType> TypeMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

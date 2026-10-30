using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Venly.Backend.Common.Authentication;
using Venly.Messaging.Events;

namespace Venly.Audit.Helper;

/// <summary>
/// Reads the actor off the gateway's principal headers.
///
/// <para>Headers rather than <c>HttpContext.User</c> claims, and that is the important choice: a downstream
/// service does not always validate the token itself — the gateway already did, and on an HMAC-only route
/// there is no token in the request at all. The headers are present on every hop the gateway signs, which
/// makes them the one source that works for both kinds of route.</para>
///
/// <para>The fallbacks are ordered so that every one of them under-claims rather than over-claims: no context
/// is System, a context with no principal is Service, and an unrecognised principal type is Service. None of
/// them can invent a staff actor.</para>
/// </summary>
public sealed class HttpContextAuditActorAccessor(
    IHttpContextAccessor accessor, IOptions<AuditActorHeaderOptions>? headerOptions = null) : IAuditActorAccessor
{
    private readonly AuditActorHeaderOptions _headers = headerOptions?.Value ?? new AuditActorHeaderOptions();

    public AuditActor Current()
    {
        var context = accessor.HttpContext;

        if (context is null)
        {
            // No request in flight: a Kafka consumer, a hosted service, a Temporal activity.
            return new AuditActor(AuditActorType.System, null, null, null, null);
        }

        var headers = context.Request.Headers;

        var id = Value(headers[_headers.IdHeader]);
        var type = Value(headers[_headers.TypeHeader]);
        var permission = Value(headers[_headers.PermissionHeader]);

        var actorType = MapType(type);

        var snapshot = permission is null
            ? null
            : new Dictionary<string, string?>
            {
                ["checked"] = permission,
                ["source"] = "gateway",
            };

        return new AuditActor(
            Type: actorType,
            Id: actorType == AuditActorType.Service ? null : id,
            PermissionsSnapshot: snapshot,
            SourceIpHash: Value(headers[_headers.SourceIpHashHeader]),
            CorrelationId: Value(headers[_headers.CorrelationIdHeader]),
            TenantId: _headers.TenantIdHeader is null ? null : Value(headers[_headers.TenantIdHeader]),
            SourceIp: _headers.SourceIpHeader is null ? null : Value(headers[_headers.SourceIpHeader]));
    }

    private AuditActorType MapType(string? type)
    {
        if (_headers.TypeMap.Count == 0)
        {
            return type switch
            {
                SendGramAuth.StaffPrincipal => AuditActorType.Staff,
                SendGramAuth.CustomerPrincipal => AuditActorType.Customer,
                _ => AuditActorType.Service,
            };
        }

        // Re-keyed case-insensitively here because configuration binding builds its own dictionary.
        foreach (var (key, value) in _headers.TypeMap)
            if (string.Equals(key, type, StringComparison.OrdinalIgnoreCase))
                return value;

        return AuditActorType.Service;
    }

    private static string? Value(Microsoft.Extensions.Primitives.StringValues values)
    {
        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

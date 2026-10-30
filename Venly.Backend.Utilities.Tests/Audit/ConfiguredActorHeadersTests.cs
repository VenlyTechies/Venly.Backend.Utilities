using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Venly.Audit.Helper;
using Venly.Backend.Common.Authentication;
using Venly.Messaging.Events;

namespace Venly.Backend.Utilities.Tests.Audit;

/// <summary>
/// A product with its own gateway names its own actor headers (AuditPublisher:ActorHeaders) instead of
/// re-implementing the accessor, and can record the tenant and the raw source IP. Unconfigured, nothing about
/// the SendGram contract changes.
/// </summary>
public class ConfiguredActorHeadersTests
{
    private static AuditActorHeaderOptions AuthetHeaders() => new()
    {
        IdHeader = "X-Authet-Audit-Actor-Id",
        TypeHeader = "X-Authet-Audit-Actor-Type",
        PermissionHeader = "X-Authet-Audit-Permission",
        TenantIdHeader = "X-Authet-Tenant",
        SourceIpHeader = "X-Authet-Source-Ip",
        TypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["operator"] = AuditActorType.Staff,
            ["tenant_user"] = AuditActorType.Customer,
        },
    };

    private static AuditActor Current(AuditActorHeaderOptions? options, params (string, string)[] headers)
    {
        var context = new DefaultHttpContext();
        foreach (var (name, value) in headers)
            context.Request.Headers[name] = value;
        var http = A.Fake<IHttpContextAccessor>();
        A.CallTo(() => http.HttpContext).Returns(context);
        return new HttpContextAuditActorAccessor(http, options is null ? null : Options.Create(options)).Current();
    }

    [Fact]
    public void Configured_headers_and_type_map_are_read()
    {
        var actor = Current(AuthetHeaders(),
            ("X-Authet-Audit-Actor-Id", "TU_ABC"),
            ("X-Authet-Audit-Actor-Type", "Tenant_User"),
            ("X-Authet-Audit-Permission", "roles.manage"),
            ("X-Authet-Tenant", "TN_1"),
            ("X-Authet-Source-Ip", "102.89.34.17"));

        Assert.Equal(AuditActorType.Customer, actor.Type);
        Assert.Equal("TU_ABC", actor.Id);
        Assert.Equal("TN_1", actor.TenantId);
        Assert.Equal("102.89.34.17", actor.SourceIp);
        Assert.Equal("roles.manage", actor.PermissionsSnapshot!["checked"]);
    }

    [Fact]
    public void An_unmapped_type_is_service_without_an_id()
    {
        var actor = Current(AuthetHeaders(),
            ("X-Authet-Audit-Actor-Id", "ST_X"), ("X-Authet-Audit-Actor-Type", "staff"));

        Assert.Equal(AuditActorType.Service, actor.Type);
        Assert.Null(actor.Id);
    }

    [Fact]
    public void Configured_product_ignores_the_sendgram_headers()
    {
        var actor = Current(AuthetHeaders(), (PrincipalHeaders.Id, "STF1"), (PrincipalHeaders.Type, "staff"));

        Assert.Equal(AuditActorType.Service, actor.Type);
        Assert.Null(actor.Id);
    }

    [Fact]
    public void Unconfigured_keeps_the_sendgram_contract_and_records_no_tenant_or_ip()
    {
        var actor = Current(null,
            (PrincipalHeaders.Id, "STF1"), (PrincipalHeaders.Type, "staff"),
            ("X-Authet-Tenant", "TN_1"), ("X-Authet-Source-Ip", "1.2.3.4"));

        Assert.Equal(AuditActorType.Staff, actor.Type);
        Assert.Equal("STF1", actor.Id);
        Assert.Null(actor.TenantId);
        Assert.Null(actor.SourceIp);
    }

    [Fact]
    public void Header_options_bind_from_configuration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuditPublisher:Enabled"] = "false",
            ["AuditPublisher:ActorHeaders:IdHeader"] = "X-Authet-Audit-Actor-Id",
            ["AuditPublisher:ActorHeaders:TenantIdHeader"] = "X-Authet-Tenant",
            ["AuditPublisher:ActorHeaders:TypeMap:operator"] = "Staff",
        }).Build();

        var options = new ServiceCollection().AddLogging().AddVenlyAuditPublisher(config)
            .BuildServiceProvider().GetRequiredService<IOptions<AuditActorHeaderOptions>>().Value;

        Assert.Equal("X-Authet-Audit-Actor-Id", options.IdHeader);
        Assert.Equal("X-Authet-Tenant", options.TenantIdHeader);
        Assert.Equal(PrincipalHeaders.Type, options.TypeHeader);
        Assert.Equal(AuditActorType.Staff, options.TypeMap["operator"]);
    }

    [Fact]
    public void Tenant_and_source_ip_round_trip_and_old_payloads_still_read()
    {
        var entry = new AuditEntryRecorded(
            ActorType: AuditActorType.Customer, ActorId: "TU_1", EffectivePermissionsSnapshot: null,
            ActionType: "role.change", EntityType: "tenant_user", EntityId: "TU_2", BeforeValue: null,
            AfterValue: null, Rationale: null, ChangeRequestId: null, EmergencyAccessId: null,
            DeniedPermissionKey: null, SourceIpHash: null, CorrelationId: null,
            OccurredAt: DateTimeOffset.UnixEpoch, TenantId: "TN_1", SourceIp: "1.2.3.4");

        var json = JsonSerializer.Serialize(entry, KafkaAuditPublisher.JsonOptions);
        var back = JsonSerializer.Deserialize<AuditEntryRecorded>(json, KafkaAuditPublisher.JsonOptions)!;
        Assert.Equal("TN_1", back.TenantId);
        Assert.Equal("1.2.3.4", back.SourceIp);

        // A message produced before these fields existed has neither property.
        var old = json.Replace(",\"TenantId\":\"TN_1\",\"SourceIp\":\"1.2.3.4\"", "");
        Assert.DoesNotContain("TenantId", old);
        var oldBack = JsonSerializer.Deserialize<AuditEntryRecorded>(old, KafkaAuditPublisher.JsonOptions)!;
        Assert.Null(oldBack.TenantId);
        Assert.Null(oldBack.SourceIp);
        Assert.Equal("role.change", oldBack.ActionType);
    }
}

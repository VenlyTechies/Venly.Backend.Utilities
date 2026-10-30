using System.Net;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Venly.Audit.Helper;
using Venly.Backend.Common;
using Venly.Messaging.Events;

namespace Venly.Backend.Utilities.Tests.Audit;

public class AuditPipelineBehaviourTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private sealed record AuditedCommand(string? EntityId)
        : IRequest<RequestResponse<string>>, IAuditable
    {
        public string AuditActionType => "customer.status.change";
        public string AuditEntityType => "customer";
        public string? AuditEntityId => EntityId;
    }

    private sealed record PlainCommand : IRequest<RequestResponse<string>>;

    private sealed class RecordingPublisher : IAuditPublisher
    {
        public List<AuditEntryRecorded> Entries { get; } = [];
        public List<AuditDecisionRecorded> Decisions { get; } = [];

        public void Publish(AuditEntryRecorded entry) => Entries.Add(entry);
        public void PublishDecision(AuditDecisionRecorded decision) => Decisions.Add(decision);
        public Task FlushAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubActorAccessor(AuditActor actor) : IAuditActorAccessor
    {
        public AuditActor Current() => actor;
    }

    private static AuditActor StaffActor => new(
        AuditActorType.Staff, "STF1",
        new Dictionary<string, string?> { ["checked"] = "customer.status.change" },
        "iphash", "corr-9");

    private static (RecordingPublisher Publisher, AuditScope Scope,
        AuditPipelineBehaviour<TRequest, RequestResponse<string>> Behaviour)
        Build<TRequest>(AuditActor? actor = null)
        where TRequest : IRequest<RequestResponse<string>>
    {
        var publisher = new RecordingPublisher();
        var scope = new AuditScope();

        var behaviour = new AuditPipelineBehaviour<TRequest, RequestResponse<string>>(
            publisher, scope, new StubActorAccessor(actor ?? StaffActor), new FakeTimeProvider(Now),
            NullLogger<AuditPipelineBehaviour<TRequest, RequestResponse<string>>>.Instance);

        return (publisher, scope, behaviour);
    }

    private static RequestHandlerDelegate<RequestResponse<string>> Handler(
        int responseCode = (int)HttpStatusCode.OK, Action? sideEffect = null) =>
        _ =>
        {
            sideEffect?.Invoke();
            return Task.FromResult(new RequestResponse<string> { ResponseCode = responseCode });
        };

    [Fact]
    public async Task An_auditable_command_that_succeeds_publishes_one_entry()
    {
        var (publisher, _, behaviour) = Build<AuditedCommand>();

        await behaviour.Handle(new AuditedCommand("CUS1"), Handler(), CancellationToken.None);

        var entry = Assert.Single(publisher.Entries);
        Assert.Equal("customer.status.change", entry.ActionType);
        Assert.Equal("customer", entry.EntityType);
        Assert.Equal("CUS1", entry.EntityId);
        Assert.Equal(AuditActorType.Staff, entry.ActorType);
        Assert.Equal("STF1", entry.ActorId);
        Assert.Equal("iphash", entry.SourceIpHash);
        Assert.Equal("corr-9", entry.CorrelationId);
        Assert.Equal(Now, entry.OccurredAt);
    }

    [Fact]
    public async Task The_actors_tenant_and_source_ip_are_carried_onto_the_entry()
    {
        var (publisher, _, behaviour) = Build<AuditedCommand>(
            StaffActor with { TenantId = "TN_1", SourceIp = "102.89.34.17" });

        await behaviour.Handle(new AuditedCommand("CUS1"), Handler(), CancellationToken.None);

        var entry = Assert.Single(publisher.Entries);
        Assert.Equal("TN_1", entry.TenantId);
        Assert.Equal("102.89.34.17", entry.SourceIp);
    }

    [Fact]
    public async Task A_tenant_set_by_the_handler_wins_over_the_actors()
    {
        var (publisher, scope, behaviour) = Build<AuditedCommand>(StaffActor with { TenantId = null });

        await behaviour.Handle(new AuditedCommand("CUS1"), Handler(sideEffect: () => scope.SetTenantId("TN_TARGET")),
            CancellationToken.None);

        Assert.Equal("TN_TARGET", Assert.Single(publisher.Entries).TenantId);
    }

    [Fact]
    public async Task A_command_that_is_not_auditable_publishes_nothing()
    {
        var (publisher, _, behaviour) = Build<PlainCommand>();

        await behaviour.Handle(new PlainCommand(), Handler(), CancellationToken.None);

        Assert.Empty(publisher.Entries);
    }

    [Fact]
    public async Task A_validation_failure_publishes_nothing()
    {
        var (publisher, _, behaviour) = Build<AuditedCommand>();

        await behaviour.Handle(
            new AuditedCommand("CUS1"), Handler((int)HttpStatusCode.BadRequest), CancellationToken.None);

        // Nothing happened, so there is nothing to audit. Recording attempted-and-rejected actions is a
        // separate feature with its own action types, not a side effect of this one.
        Assert.Empty(publisher.Entries);
    }

    [Fact]
    public async Task A_server_error_publishes_nothing()
    {
        var (publisher, _, behaviour) = Build<AuditedCommand>();

        await behaviour.Handle(
            new AuditedCommand("CUS1"), Handler((int)HttpStatusCode.InternalServerError),
            CancellationToken.None);

        Assert.Empty(publisher.Entries);
    }

    [Fact]
    public async Task A_thrown_handler_publishes_nothing_and_the_exception_still_propagates()
    {
        var (publisher, _, behaviour) = Build<AuditedCommand>();

        RequestHandlerDelegate<RequestResponse<string>> throwing =
            _ => throw new InvalidOperationException("handler blew up");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behaviour.Handle(new AuditedCommand("CUS1"), throwing, CancellationToken.None));

        Assert.Empty(publisher.Entries);
    }

    [Fact]
    public async Task The_scope_supplies_the_entity_id_the_command_could_not_know()
    {
        var (publisher, scope, behaviour) = Build<AuditedCommand>();

        // The create case: the command has no id, the handler assigns one.
        await behaviour.Handle(
            new AuditedCommand(null), Handler(sideEffect: () => scope.SetEntityId("CUS-NEW")),
            CancellationToken.None);

        Assert.Equal("CUS-NEW", Assert.Single(publisher.Entries).EntityId);
    }

    [Fact]
    public async Task The_scope_supplies_before_and_after_values()
    {
        var (publisher, scope, behaviour) = Build<AuditedCommand>();

        await behaviour.Handle(new AuditedCommand("CUS1"), Handler(sideEffect: () =>
        {
            scope.SetBefore(new { status = "ACTIVE" });
            scope.SetAfter(new { status = "SUSPENDED" });
            scope.SetRationale("Fraud review.");
        }), CancellationToken.None);

        var entry = Assert.Single(publisher.Entries);
        Assert.Equal("""{"status":"ACTIVE"}""", entry.BeforeValue);
        Assert.Equal("""{"status":"SUSPENDED"}""", entry.AfterValue);
        Assert.Equal("Fraud review.", entry.Rationale);
    }

    [Fact]
    public async Task A_suppressed_scope_publishes_nothing()
    {
        var (publisher, scope, behaviour) = Build<AuditedCommand>();

        await behaviour.Handle(
            new AuditedCommand("CUS1"), Handler(sideEffect: scope.Suppress), CancellationToken.None);

        Assert.Empty(publisher.Entries);
    }

    [Fact]
    public async Task A_publisher_failure_does_not_fail_the_request()
    {
        var scope = new AuditScope();

        var behaviour = new AuditPipelineBehaviour<AuditedCommand, RequestResponse<string>>(
            new ThrowingPublisher(), scope, new StubActorAccessor(StaffActor), new FakeTimeProvider(Now),
            NullLogger<AuditPipelineBehaviour<AuditedCommand, RequestResponse<string>>>.Instance);

        var response = await behaviour.Handle(
            new AuditedCommand("CUS1"), Handler(), CancellationToken.None);

        // The handler already committed. Turning that into a 500 would lose the action as well as the audit.
        Assert.Equal((int)HttpStatusCode.OK, response.ResponseCode);
    }

    private sealed class ThrowingPublisher : IAuditPublisher
    {
        public void Publish(AuditEntryRecorded entry) => throw new InvalidOperationException("no broker");
        public void PublishDecision(AuditDecisionRecorded decision) => throw new InvalidOperationException();
        public Task FlushAsync(TimeSpan timeout, CancellationToken ct = default) => Task.CompletedTask;
    }
}

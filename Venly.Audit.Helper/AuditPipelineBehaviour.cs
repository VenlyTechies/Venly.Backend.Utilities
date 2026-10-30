using MediatR;
using Microsoft.Extensions.Logging;
using Venly.Backend.Common;
using Venly.Messaging.Events;

namespace Venly.Audit.Helper;

/// <summary>
/// Turns an <see cref="IAuditable"/> marker into a published audit event.
///
/// <para>It runs AFTER the handler and only on success, which is the whole design: an audit row is a claim
/// that something happened, so publishing before the handler — or regardless of its result — would fill the
/// table with actions that were rejected, rolled back, or never attempted.</para>
///
/// <para>Registered with <c>config.AddOpenBehavior(typeof(AuditPipelineBehaviour&lt;,&gt;))</c> AFTER
/// <c>ValidatePipelineBehaviour</c>, so a request that fails validation short-circuits before reaching this at
/// all. The 400-and-above check below is the second line of defence, for a handler that returns a failure
/// code of its own.</para>
///
/// <para>The constraints mirror <see cref="Venly.Backend.Common.Pipelines.ValidatePipelineBehaviour{TRequest,
/// TResponse}"/>. MediatR only applies an open behaviour to requests that satisfy them, so a request outside
/// the RequestResponse convention simply does not get this behaviour rather than failing to register.</para>
/// </summary>
public sealed class AuditPipelineBehaviour<TRequest, TResponse>(
    IAuditPublisher publisher,
    IAuditScope scope,
    IAuditActorAccessor actors,
    TimeProvider time,
    ILogger<AuditPipelineBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IRequestResponse
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Not wrapped in a try/catch: a throwing handler must propagate untouched, and an exception means the
        // action did not complete, so there is nothing to audit.
        var response = await next(cancellationToken);

        if (request is not IAuditable auditable)
            return response;

        if (response is null || response.ResponseCode >= 400)
            return response;

        if (scope.Suppressed)
            return response;

        try
        {
            var actor = actors.Current();

            publisher.Publish(new AuditEntryRecorded(
                ActorType: actor.Type,
                ActorId: actor.Id,
                EffectivePermissionsSnapshot: actor.PermissionsSnapshot,
                ActionType: auditable.AuditActionType,
                EntityType: auditable.AuditEntityType,

                // The scope wins. It is set by the handler, which knows the id of a row it has just created;
                // the command's own value is what was known before the handler ran.
                EntityId: scope.EntityId ?? auditable.AuditEntityId,
                BeforeValue: scope.BeforeValue,
                AfterValue: scope.AfterValue,
                Rationale: scope.Rationale,
                ChangeRequestId: scope.ChangeRequestId,
                EmergencyAccessId: scope.EmergencyAccessId,

                // Only the gateway's denial path sets this, and it does not go through MediatR.
                DeniedPermissionKey: null,
                SourceIpHash: actor.SourceIpHash,
                CorrelationId: actor.CorrelationId,
                OccurredAt: time.GetUtcNow(),
                TenantId: actor.TenantId,
                SourceIp: actor.SourceIp));
        }
        catch (Exception ex)
        {
            // Same reasoning as KafkaAuditPublisher.Send: the handler has already committed, so throwing here
            // would lose the action as well as the audit record.
            logger.LogError(ex,
                "Could not publish an audit event for {Request} ({Action} on {EntityType}). The action itself "
                + "succeeded and is unaffected.",
                typeof(TRequest).Name, auditable.AuditActionType, auditable.AuditEntityType);
        }

        return response;
    }
}

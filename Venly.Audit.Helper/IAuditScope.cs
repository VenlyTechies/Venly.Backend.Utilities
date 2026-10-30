namespace Venly.Audit.Helper;

/// <summary>
/// Per-request enrichment for the audit event the pipeline behaviour is about to publish. Registered SCOPED,
/// so a handler and the behaviour wrapping it share one instance.
///
/// <para>This is the half of an audit record a command declaration cannot supply: the id of a row created
/// during the handler, the state before it was changed, the rationale the caller typed. A handler that needs
/// none of them injects nothing and the behaviour publishes what the command declared.</para>
/// </summary>
public interface IAuditScope
{
    string? EntityId { get; }
    string? BeforeValue { get; }
    string? AfterValue { get; }
    string? Rationale { get; }
    string? ChangeRequestId { get; }
    string? EmergencyAccessId { get; }

    /// <summary>True when the handler decided this particular execution should not be audited.</summary>
    bool Suppressed { get; }

    /// <summary>
    /// The tenant the action was ABOUT, when that is not the caller's own tenant — an operator approving a
    /// tenant's live access, suspending a tenant. Wins over the actor's tenant. Default: not set.
    /// </summary>
    string? TenantId => null;

    void SetEntityId(string? entityId);

    /// <summary>Sets <see cref="TenantId"/>. Default implementation ignores it (single-tenant products).</summary>
    void SetTenantId(string? tenantId) { }

    /// <summary>Serialises <paramref name="value"/> to JSON for the jsonb column. A null leaves the column
    /// null rather than writing the JSON literal <c>null</c>.</summary>
    void SetBefore(object? value);

    void SetAfter(object? value);

    void SetRationale(string? rationale);

    void SetChangeRequestId(string? changeRequestId);

    void SetEmergencyAccessId(string? emergencyAccessId);

    /// <summary>
    /// Skips publication for this execution. For the case where a command succeeded but changed nothing —
    /// an idempotent re-submit, a no-op update — and an audit row would claim an action that did not happen.
    /// </summary>
    void Suppress();
}

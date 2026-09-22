namespace Venly.Wallet.Helper;

/// <summary>
/// The SCHEDULED work WorkflowService drives inside WalletService. HMAC, over <c>/internal/wallet/*</c>.
///
/// <para>Deliberately separate from any per-movement intent client. Maintenance is scheduled work and an intent
/// is a movement of somebody's money; one interface carrying both would give WorkflowService the ability to
/// confirm a payment, which is not a capability a scheduler needs and not one worth granting by accident.</para>
/// </summary>
public interface IWalletMaintenanceClient
{
    /// <summary>
    /// Releases reservations past their window. Idempotent and bounded, so a retry costs nothing and a backlog
    /// drains oldest-first rather than stalling the service.
    /// </summary>
    Task<ExpireIntentsResult> ExpireReservationsAsync(int batchSize, CancellationToken ct = default);

    /// <param name="currency">
    /// Null runs every currency. Named, only that one — so an operator can re-run one during an incident.
    /// </param>
    Task<List<ReconciliationRunSummary>> RunReconciliationAsync(
        string? currency, CancellationToken ct = default);

    Task<SampleFxPositionResultBody> SampleFxPositionsAsync(
        SampleFxPositionRequestBody request, CancellationToken ct = default);

    Task<IngestProviderBalancesResultBody> IngestProviderBalancesAsync(
        IngestProviderBalancesRequestBody request, CancellationToken ct = default);

    Task<List<SafeguardingSnapshotSummary>> GenerateSafeguardingSnapshotsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Keeps one rate sample. The RAW provider rate — WalletService stamps the margin in force onto the row
    /// itself, so history can later be drawn at what was actually displayed rather than at today's figure.
    ///
    /// <para><b>Split from the provider call on purpose.</b> Asking a provider what a route costs is
    /// integration and stays in PaymentService, which makes the call and logs it to <c>provider_request</c>.
    /// Keeping the answer is business state and belongs with the ledger. The workflow carries the value
    /// between them, so a corridor whose provider is unavailable fails alone and the others still store.</para>
    /// </summary>
    Task<StoreRateSnapshotResultBody> StoreRateSnapshotAsync(
        StoreRateSnapshotBody body, CancellationToken ct = default);

    /// <summary>
    /// Checks every active rate alert against the newest sample and notifies whoever it has crossed.
    ///
    /// <para>Called immediately after the samples are stored, in the SAME workflow, because it reads what
    /// the capture just wrote — a separate schedule could interleave and evaluate a sample one tick stale.
    /// It fires on the DISPLAYED rate, since that is the number the customer set the alert against.</para>
    /// </summary>
    Task<RateAlertRunResultBody> EvaluateRateAlertsAsync(CancellationToken ct = default);
}

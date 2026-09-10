namespace Venly.Rails.Helper;

/// <summary>
/// PaymentService's periodic rate capture, over HMAC — what WorkflowService's
/// <c>payment-snapshot-rates</c> schedule drives.
///
/// <para>
/// SEPARATE from <see cref="IRailsClient"/> on purpose. That interface is read-only by design, and the
/// promise is load-bearing: WalletService's reconciliation depends on it, and a ledger able to instruct
/// through the same client is a ledger able to move money without an intent. This one WRITES — it takes a
/// sample and spends a provider quote doing so — so it is its own interface with its own callers, exactly as
/// <c>IWalletMaintenanceClient</c> is separate from the wallet's read surface.
/// </para>
/// <para>
/// One implementation and one signing path serve both: the target service, the secret and the HTTP client are
/// the same. What differs is which capability a caller has to ask for.
/// </para>
/// </summary>
public interface IRatesMaintenanceClient
{
    /// <summary>
    /// Samples every configured currency pair and keeps the result.
    ///
    /// <para>
    /// Answers a SUMMARY rather than throwing on a pair that failed: the tick ran, and a partial success is
    /// the normal case when one corridor's provider is unavailable. The activity logs what came back and
    /// lets the next tick try the rest — retrying the whole tick would re-spend the quotes that succeeded.
    /// </para>
    /// </summary>
    Task<RateSnapshotSummaryResult> SnapshotRatesAsync(CancellationToken ct = default);
}

/// <param name="Captured">Rows written this tick.</param>
/// <param name="Skipped">Pairs that already had a row for this minute — a re-fired tick, not a problem.</param>
/// <param name="Failures">One message per pair that could not be sampled.</param>
public sealed record RateSnapshotSummaryResult(
    int Captured,
    int Skipped,
    List<string> Failures);

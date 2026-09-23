namespace Venly.Rails.Helper;

/// <summary>
/// PaymentService's rails surface, over HMAC. What WalletService's reconciliation PULLS through.
///
/// <para><b>It instructs as well as reads, and the old rule survives as a requirement on the caller.</b> This
/// was read-only, and said so: "there is no payout here and there must not be: WalletService is the ledger,
/// and a ledger that could instruct a payment could move money without an intent." WalletService owns
/// transfers and funding now, so it has to instruct — and what protected the invariant was never the missing
/// method, it was the invariant. Every write here carries the intent it settles, and PaymentService refuses
/// one that does not.</para>
///
/// <para><b>Create the intent FIRST, then call.</b> An intent id naming an intent that does not exist yet
/// passes the check and buys nothing; the check is a guard against forgetting the ledger, not a substitute
/// for it.</para>
///
/// <para>A requery and a quote take no intent id, because neither moves money.</para>
///
/// <para><b>EVERY method names a rail, and there is no default.</b> Which rail serves a corridor is
/// WalletService's decision, made per call from its own <c>Rail</c> / <c>RailCorridorMapping</c> tables in
/// priority order — PaymentService is a wrapper over the rails and chooses nothing. A rail code this build of
/// PaymentService does not carry comes back 422 rather than being quietly swapped for a fallback nobody
/// picked.</para>
/// </summary>
public interface IRailsClient
{
    /// <summary>
    /// What the provider says it holds. Fed straight into <c>IngestProviderBalances</c>, which is why the
    /// figures are already in minor units and the provider's name comes with them.
    /// </summary>
    Task<RailsBalancesResult> GetBalancesAsync(string rail, CancellationToken ct = default);

    /// <summary>
    /// The rail codes this build of PaymentService carries.
    ///
    /// <para>What replaced PaymentService's startup validation of <c>Rails:ByCorridor</c>. WalletService
    /// checks an admin-supplied <c>Rail.Code</c> against this AT THE WRITE, not at boot: validating at
    /// startup would make WalletService unbootable whenever PaymentService is down, which is a worse failure
    /// than the typo it would catch.</para>
    /// </summary>
    Task<RailsRegistryResult> GetRegistryAsync(CancellationToken ct = default);

    Task<List<RailsStatementLineResult>> GetStatementAsync(
        string rail, string currency, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<List<RailsBankResult>> GetBanksAsync(
        string rail, string currency, string country, CancellationToken ct = default);

    Task<RailsAccountNameResult> ResolveAccountAsync(
        string rail, string accountNumber, string bankCode, CancellationToken ct = default);

    // ---- writes ------------------------------------------------------------------------------------

    /// <summary>
    /// Prices a route. Spends a provider call and moves nothing, so it needs no intent — WalletService quotes
    /// to show a customer a number before any intent exists.
    ///
    /// <para>The quote is PERISHABLE. Fetch it immediately before the call that spends it rather than holding
    /// one across a customer's decision.</para>
    /// </summary>
    Task<RailsQuoteResult> GenerateQuoteAsync(
        RailsQuoteRequestBody body, CancellationToken ct = default);

    /// <summary>
    /// Instructs a payout on the NAMED rail. Comes back <c>Processing</c>: settlement arrives later by
    /// webhook, and a caller written against a synchronous success would confirm intents that never paid.
    ///
    /// <para><b>A rail that would not pay does NOT throw — it answers.</b> The result carries
    /// <see cref="RailsOutcome"/>, and only <see cref="RailsOutcome.Rejected"/> permits trying the next rail
    /// down. <see cref="RailsOutcome.Indeterminate"/> means the instruction may already be in flight; failing
    /// over on it instructs a second payout against one intent. An exception from this method means
    /// PAYMENTSERVICE broke, not that the rail refused — the two are different failures and this is the line
    /// between them.</para>
    /// </summary>
    Task<RailsPayoutAckResult> InitiatePayoutAsync(
        RailsPayoutRequestBody body, CancellationToken ct = default);

    /// <summary>
    /// Asks what became of a payout by OUR reference. The right response to a timeout — which may have been
    /// acted on with only the response lost — and to a duplicate-reference rejection. Takes no intent id: it
    /// instructs nothing, and requiring one to ask would leave a caller unable to find out what happened to
    /// money already in flight.
    /// </summary>
    Task<RailsPayoutAckResult> RequeryPayoutAsync(
        string rail, string ourReference, CancellationToken ct = default);

    /// <summary>Executes a quoted FX conversion. Carries an intent: it moves our own currency positions.</summary>
    Task<RailsConversionResult> InitiateConversionAsync(
        RailsConversionRequestBody body, CancellationToken ct = default);

    /// <summary>
    /// Creates a hosted checkout page and returns the link to send the customer to. Credits nothing — the
    /// money arrives later and the <c>charge.successful</c> webhook turns it into a balance.
    /// </summary>
    Task<RailsCheckoutResult> CreateCheckoutAsync(
        RailsCheckoutRequestBody body, CancellationToken ct = default);
}

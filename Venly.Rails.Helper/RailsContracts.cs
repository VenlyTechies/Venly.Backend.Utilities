namespace Venly.Rails.Helper;

/// <summary>
/// The wire contracts for PaymentService's <c>/internal/payment/rails</c> surface, shared by the service that
/// serves them and WalletService's reconciliation, which pulls through them.
///
/// <para>MINOR UNITS, because that is what the ledger speaks and PaymentService has already converted at the
/// provider boundary. A contract in major units here would put the conversion in two places.</para>
/// </summary>
/// <param name="RollingReserveMinor">
/// Reported and reconciled against NOTHING: SendGram has no account for a rolling reserve. Null means the
/// provider did not say, which is a different fact from zero.
/// </param>
public sealed record RailsBalanceSnapshot(
    string Currency,
    long AvailableMinor,
    long LockedMinor,
    long LedgerMinor,
    long? RollingReserveMinor,
    DateTime AsAt);

/// <param name="Provider">
/// <c>"fincra"</c> or <c>"stub"</c>. Carried through to <c>external_balance_snapshot.Source</c>, so a
/// reconciliation run against the stub is distinguishable from one against the real provider.
/// </param>
public sealed record RailsBalancesResult(string Provider, List<RailsBalanceSnapshot> Balances);

public sealed record RailsStatementLineResult(
    string Reference,
    string Currency,
    long AmountMinor,
    string Direction,
    string? Narration,
    DateTime PostedAt);

public sealed record RailsBankResult(string Code, string Name, string? Type);

public sealed record RailsAccountNameResult(
    string AccountNumber, string BankCode, string AccountHolderName);

/// <param name="Rail">Which rail is asked. WalletService picks it; there is no default to fall back on.</param>
public sealed record ResolveAccountRequestBody(string Rail, string AccountNumber, string BankCode);

/// <summary>
/// HOW A RAIL CALL ENDED, as distinct from what the provider said about the payment.
///
/// <para><b>This is the value failover turns on, and getting it wrong double-pays a customer.</b>
/// WalletService walks a corridor's rails in priority order; this says whether walking on is safe.</para>
/// </summary>
public enum RailsOutcome
{
    /// <summary>
    /// The rail took the instruction. <see cref="RailsPayoutAckResult.Status"/> says what it then did with
    /// it. Failover is OVER — there is nothing left to fail over to, the money is on this rail.
    /// </summary>
    Accepted,

    /// <summary>
    /// The rail refused BEFORE accepting anything: a refused connection, a 4xx validation, a beneficiary it
    /// would not pay. Nothing is in flight, so the next rail down may be tried.
    ///
    /// <para>Only a failure the rail itself answered may be classified here. A failure we INFERRED is
    /// <see cref="Indeterminate"/>.</para>
    /// </summary>
    Rejected,

    /// <summary>
    /// We do not know, and must not guess: a timeout, a 5xx, a duplicate-reference rejection. The instruction
    /// may have been acted on with only the response lost.
    ///
    /// <para><b>FAILOVER STOPS HERE.</b> Trying the next rail would be instructing a second payout against an
    /// intent whose first payout may already be settling. The intent stays reserved and the requery path owns
    /// it — which is why a requery takes no intent id and names the rail that was attempted.</para>
    /// </summary>
    Indeterminate,
}

/// <summary>
/// What every money-moving result has to carry for failover to be able to read it.
///
/// <para>An interface rather than a pair of delegates at the call site, because these two fields are not
/// incidental to a payout result — they are the part WalletService acts on. A new instruction type that
/// forgets them will not compile against <c>IRailRouter.InstructAsync</c>, which is the correct place to find
/// out: the alternative is a result whose failure silently reads as <c>Indeterminate</c> with no reason, and
/// stops a walk nobody could then explain.</para>
/// </summary>
public interface IRailsAttempt
{
    /// <summary>Accepted, Rejected or Indeterminate. Only Rejected permits trying the next rail.</summary>
    RailsOutcome Outcome { get; }

    /// <summary>What the rail said, where there was anything to say. Null on an accepted attempt.</summary>
    string? FailureReason { get; }
}

/// <param name="Rails">
/// The rail codes THIS BUILD of PaymentService carries. WalletService checks a <c>Rail.Code</c> against it
/// when an admin creates or renames one, so a typo is refused at the write rather than at the first payout.
/// </param>
public sealed record RailsRegistryResult(List<string> Rails);

// ---- the write surface -----------------------------------------------------------------------------
//
// PaymentService's /internal/payment/rails surface INSTRUCTS as well as reads, now that WalletService owns
// transfers and funding and must drive a payout through it.
//
// Every body that moves money OUT carries IntentId FIRST, and PaymentService refuses one without it. The old
// surface was read-only so that nothing could pay without an intent; the rule survives the routes as a check
// rather than as an absence, and putting the field first makes a body that lacks it obvious on sight.
//
// A quote, a requery and a CHECKOUT carry none, and that is the same rule rather than three exceptions to it:
// a quote prices a route, a requery asks what already happened, and a checkout opens a page for money to come
// IN. None of them takes money out, and a checkout could not name an intent even if asked to -- the intent is
// created when the charge.successful webhook says the money arrived.

/// <param name="IntentId">
/// The WalletService intent this payout settles. REQUIRED: a payout with no intent is money leaving the system
/// with nothing in the ledger reserving it.
/// </param>
/// <param name="OurReference">
/// The intent's own reference, sent as the provider's <c>customerReference</c>. Unique per payout, and what a
/// requery is performed by — the single string correlating the reservation, the instruction and the settlement.
/// </param>
public sealed record RailsPayoutRequestBody(
    string IntentId,
    string Rail,
    string OurReference,
    string SourceCurrency,
    string DestinationCurrency,
    long AmountMinor,
    string? QuoteReference,
    string AccountHolderName,
    string AccountNumber,
    string BankCode,
    string Country,
    string BeneficiaryType,
    string? Description);

/// <param name="DocumentRequired">
/// The provider may hold a payout pending paperwork. Carried rather than swallowed: a payout waiting on a
/// document is not one that failed, and treating it as failed would release a reservation still live on their
/// side.
/// </param>
public sealed record RailsPayoutAckResult(
    string Rail,
    RailsOutcome Outcome,
    string ProviderReference,
    string OurReference,
    string Status,
    bool DocumentRequired,
    string? FailureReason) : IRailsAttempt;

/// <param name="IntentId">The intent whose FX leg this conversion is. REQUIRED, for the same reason.</param>
public sealed record RailsConversionRequestBody(
    string IntentId,
    string Rail,
    string QuoteReference,
    string OurReference);

public sealed record RailsConversionResult(
    string Rail,
    RailsOutcome Outcome,
    string ProviderReference,
    string OurReference,
    string Status,
    decimal? Rate,
    string? FailureReason) : IRailsAttempt;

/// <remarks>
/// NO intent id, unlike a payout or a conversion, and the asymmetry is the rule rather than an exception. The
/// rule is that nothing takes money OUT without an intent reserving it; a checkout is inbound. It opens a page
/// for money to come in, reserves nothing and credits nothing — the intent is created when the
/// <c>charge.successful</c> webhook says the money actually arrived. Requiring one here would demand an intent
/// for a payment that may never be made.
/// </remarks>
/// <param name="Methods">Card only, today. The hosted page is what keeps card details out of this system.</param>
public sealed record RailsCheckoutRequestBody(
    string Rail,
    string OurReference,
    string Currency,
    long AmountMinor,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    IReadOnlyList<string> Methods,
    string? RedirectUrl,
    string? Description);

/// <param name="Link">The hosted page. Nothing happens until the customer reaches it.</param>
public sealed record RailsCheckoutResult(string Rail, string Link, string OurReference, string PayCode);

/// <summary>
/// Prices a route. NO intent id: WalletService quotes to show a customer a number before any intent exists, so
/// requiring one would make the first step of a transfer impossible. It spends a provider call and moves
/// nothing.
/// </summary>
public sealed record RailsQuoteRequestBody(
    string Rail,
    string SourceCurrency,
    string DestinationCurrency,
    long SourceMinor);

/// <param name="ExpiresAt">
/// Quotes are PERISHABLE — thirty seconds at Fincra. Carried so a caller can refuse to spend a stale one
/// rather than discovering it at the payout.
/// </param>
/// <param name="Rail">
/// The rail that priced this, carried back because <see cref="Reference"/> is MEANINGLESS anywhere else — a
/// quote reference is a handle inside one provider. The payout that spends it must name this same rail, so
/// failover between the quote and the payout is failover that invalidates the quote.
/// </param>
public sealed record RailsQuoteResult(
    string Rail,
    string Reference,
    string SourceCurrency,
    string DestinationCurrency,
    long SourceMinor,
    long DestinationMinor,
    decimal Rate,
    long FeeMinor,
    string FeeCurrency,
    DateTime ExpiresAt);

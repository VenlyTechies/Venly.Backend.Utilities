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

public sealed record ResolveAccountRequestBody(string AccountNumber, string BankCode);

// ---- the write surface -----------------------------------------------------------------------------
//
// PaymentService's /internal/payment/rails surface INSTRUCTS as well as reads, now that WalletService owns
// transfers and funding and must drive a payout through it.
//
// Every body that moves money carries IntentId FIRST, and PaymentService refuses one without it. The old
// surface was read-only so that nothing could pay without an intent; the rule survives the routes as a check
// rather than as an absence, and putting the field first makes a body that lacks it obvious on sight.

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
    string ProviderReference,
    string OurReference,
    string Status,
    bool DocumentRequired,
    string? FailureReason);

/// <param name="IntentId">The intent whose FX leg this conversion is. REQUIRED, for the same reason.</param>
public sealed record RailsConversionRequestBody(
    string IntentId,
    string QuoteReference,
    string OurReference);

public sealed record RailsConversionResult(
    string ProviderReference,
    string OurReference,
    string Status,
    decimal? Rate,
    string? FailureReason);

/// <param name="IntentId">The funding intent this checkout will credit. REQUIRED.</param>
/// <param name="Methods">Card only, today. The hosted page is what keeps card details out of this system.</param>
public sealed record RailsCheckoutRequestBody(
    string IntentId,
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
public sealed record RailsCheckoutResult(string Link, string OurReference, string PayCode);

/// <summary>
/// Prices a route. NO intent id: WalletService quotes to show a customer a number before any intent exists, so
/// requiring one would make the first step of a transfer impossible. It spends a provider call and moves
/// nothing.
/// </summary>
public sealed record RailsQuoteRequestBody(
    string SourceCurrency,
    string DestinationCurrency,
    long SourceMinor);

/// <param name="ExpiresAt">
/// Quotes are PERISHABLE — thirty seconds at Fincra. Carried so a caller can refuse to spend a stale one
/// rather than discovering it at the payout.
/// </param>
public sealed record RailsQuoteResult(
    string Reference,
    string SourceCurrency,
    string DestinationCurrency,
    long SourceMinor,
    long DestinationMinor,
    decimal Rate,
    long FeeMinor,
    string FeeCurrency,
    DateTime ExpiresAt);

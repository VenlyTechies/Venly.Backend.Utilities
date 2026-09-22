namespace Venly.Wallet.Helper;

/// <summary>
/// What WalletService answered, WITHOUT throwing on a non-2xx.
///
/// <para>Deliberately not <c>EnsureSuccessStatusCode</c>, unlike the maintenance client. A webhook handler has
/// to be able to tell a 409 from a 422 and treat them differently: a 409 on confirm means the intent was ALREADY
/// posted, which is the expected outcome of a duplicate webhook delivery and therefore a success, while a 422
/// means the reservation expired and a human is needed. A client that threw on both would collapse that
/// distinction into "the call failed".</para>
/// </summary>
public sealed record WalletCallResult(int StatusCode, string? Message)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// The intent was already in the requested state. On confirm this is what a REPLAYED webhook produces, and
    /// treating it as success is what makes duplicate delivery harmless.
    /// </summary>
    public bool IsConflict => StatusCode == 409;

    /// <summary>
    /// The movement could not be applied — an expired reservation, most often. The money moved at the provider
    /// and the reservation did not survive, which needs a person.
    /// </summary>
    public bool IsUnprocessable => StatusCode == 422;

    public bool IsNotFound => StatusCode == 404;
}

/// <summary>Enough of an intent to decide what to do with a webhook about it.</summary>
public sealed record WalletIntentLookup(
    string IntentId,
    string Reference,
    string Status,
    string Kind,
    string Currency,
    decimal Amount,
    string DestinationCurrency,
    decimal DestinationAmount,
    decimal? QuotedRate);

/// <param name="IdempotencyKey">
/// The provider's own reference. A retried collection webhook then returns the FIRST intent rather than
/// reserving a second time — which is the whole reason WalletService takes one.
/// </param>
public sealed record CreateFundingIntentRequest(
    string CustomerId,
    string Currency,
    decimal Amount,
    string IdempotencyKey,
    string? CorrelationId,
    string? Narration);

/// <summary>
/// Reserves an OUTBOUND intent: money leaving the customer's wallet to an external bank account.
///
/// <para>The mirror of <see cref="CreateFundingIntentRequest"/>, and the difference that matters is which way
/// the reservation runs. Funding reserves a credit that has not landed; this moves available to LOCKED, so the
/// customer cannot spend the same balance twice while the payout is in flight. WalletService refuses the
/// reservation outright when the balance is not there, which is what makes "insufficient funds" an answer the
/// caller gets before any provider is contacted.</para>
///
/// <para><paramref name="DestinationCurrency"/> and <paramref name="DestinationAmount"/> differ from the source
/// pair only on a cross-currency send, where the movement posts as two balanced legs joined by a bridge. Pass
/// the same values on both sides for a same-currency payout.</para>
/// </summary>
/// <param name="IdempotencyKey">
/// A retried create returns the EXISTING intent rather than reserving a second time, so this is what stops a
/// customer double-tapping Send from locking their balance twice.
/// </param>
/// <param name="QuotedRate">
/// Indicative only, and stored for the statement. It is NOT what the movement settles at: a provider quote is
/// valid for thirty seconds, so the executable rate is fetched immediately before the payout call and the
/// realised figure arrives later on the webhook.
/// </param>
public sealed record CreatePayoutIntentRequest(
    string CustomerId,
    string Currency,
    decimal Amount,
    string DestinationCurrency,
    decimal DestinationAmount,
    decimal? QuotedRate,
    string? QuoteSource,
    DateTime? QuoteExpiresAt,
    string IdempotencyKey,
    string? CorrelationId,
    string? Narration);

/// <summary>
/// Reserves a wallet-to-wallet movement inside SendGram.
///
/// <para>No payout leg and no provider: the money never leaves the system, so there is nothing to instruct and
/// nothing to wait for. The caller confirms it immediately rather than waiting on a webhook that will never
/// arrive — which is the whole reason this is a separate shape from a payout rather than a flag on one.</para>
/// </summary>
public sealed record CreateInternalTransferIntentRequest(
    string CustomerId,
    string DestinationCustomerId,
    string Currency,
    decimal Amount,
    string IdempotencyKey,
    string? CorrelationId,
    string? Narration);

/// <param name="AmountMinor">
/// What ARRIVED, in preference to what was asked for. The two can differ, and the ledger has to match the
/// money rather than the intention — crediting the requested figure is how a balance drifts from the provider
/// float by every fee and every part-payment.
/// </param>
/// <param name="ProviderReference">The provider's own handle for the charge, kept for support conversations.</param>
/// <param name="Provider">Which rail reported it, recorded as who posted the intent.</param>
public sealed record SettleFundingRequest(
    string Reference,
    long AmountMinor,
    string Currency,
    string? ProviderReference,
    string Provider);

/// <param name="Credited">
/// False where nothing was posted — an unknown reference, or a redelivery of a top-up already completed.
/// Neither is an error; both mean the caller has nothing left to do.
/// </param>
public sealed record SettleFundingResult(bool Credited, string? IntentId);

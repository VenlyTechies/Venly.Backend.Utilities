namespace Venly.Wallet.Helper;

/// <summary>
/// The PER-MOVEMENT intent calls, over HMAC.
///
/// <para>Separate from <see cref="IWalletMaintenanceClient"/> on purpose, and the separation is a capability
/// boundary rather than tidiness: maintenance is scheduled work, an intent is somebody's money, and one
/// interface carrying both would give the scheduler the ability to confirm a payment. WorkflowService holds the
/// maintenance client; PaymentService holds this one.</para>
/// </summary>
public interface IWalletIntentClient
{
    /// <summary>
    /// Finds an intent by the reference the provider echoed back as <c>customerReference</c>. Null when no
    /// movement in this system carries it — a webhook for a payout we never made is not an error.
    /// </summary>
    Task<WalletIntentLookup?> FindByReferenceAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Posts the movement. A 409 means it was already posted, which is what a duplicate delivery produces.
    /// </summary>
    Task<WalletCallResult> ConfirmAsync(
        string intentId, string? postedBy, CancellationToken ct = default);

    /// <param name="reason">
    /// The provider's OWN message, passed through verbatim. A customer-facing failure reason must be what the
    /// provider said and not a paraphrase of it.
    /// </param>
    Task<WalletCallResult> FailAsync(string intentId, string reason, CancellationToken ct = default);

    /// <summary>Reserves a funding intent for an inbound collection.</summary>
    Task<(WalletCallResult Result, WalletIntentLookup? Intent)> CreateFundingAsync(
        CreateFundingIntentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reserves a PAYOUT intent: the customer is sending money out to an external account.
    ///
    /// <para>This is the call that makes a send possible at all, and it belongs on this interface rather than on
    /// the rails client for the reason stated there — WalletService is the ledger and must never be able to
    /// instruct a payment. The direction stays PaymentService to WalletService: this service reserves the funds
    /// here, instructs its own provider, and confirms this intent when the provider's webhook says the money
    /// landed.</para>
    ///
    /// <para>A refusal is a normal outcome, not an exception: an insufficient balance comes back as a non-2xx
    /// with the ledger's own message, and the caller must not contact a provider after one.</para>
    /// </summary>
    Task<(WalletCallResult Result, WalletIntentLookup? Intent)> CreatePayoutAsync(
        CreatePayoutIntentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Reserves a wallet-to-wallet movement inside SendGram, which settles without any provider.
    /// </summary>
    Task<(WalletCallResult Result, WalletIntentLookup? Intent)> CreateInternalTransferAsync(
        CreateInternalTransferIntentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Releases a reservation the caller decided not to proceed with.
    ///
    /// <para>Distinct from <see cref="FailAsync"/>, and the distinction is worth keeping: a failure is the
    /// provider refusing a payment that was attempted, a cancellation is us never attempting it — because the
    /// quote expired, or the payout instruction itself was rejected before any money moved. Both release the
    /// reservation; only one of them means something went wrong downstream.</para>
    /// </summary>
    Task<WalletCallResult> CancelAsync(
        string intentId, string reason, CancellationToken ct = default);

    /// <param name="realisedRate">
    /// What the provider ACTUALLY executed at, in destination units per one source unit. Not the quote.
    /// </param>
    Task<WalletCallResult> RecordFxRealisationAsync(
        string intentId, decimal realisedRate, string? providerReference, CancellationToken ct = default);

    /// <summary>
    /// Tells WalletService that a top-up reference has been PAID, and lets it do the crediting.
    ///
    /// <para><b>A fact forwarded, not an instruction assembled.</b> PaymentService's charge webhook used to
    /// read <c>funding_request</c> itself, resolve whose money it was, and then drive two calls here to create
    /// and confirm the intent. That row lives in the ledger's schema now, so the webhook cannot resolve a
    /// reference to a customer at all — and a webhook handler that could would be reaching across a service
    /// boundary into the ledger's tables.</para>
    ///
    /// <para>So this carries only what the PROVIDER said: which reference, how much actually arrived, in what
    /// currency. WalletService resolves the rest. It also collapses three round trips into one.</para>
    /// </summary>
    Task<WalletCallResult> SettleFundingAsync(
        SettleFundingRequest request, CancellationToken ct = default);
}

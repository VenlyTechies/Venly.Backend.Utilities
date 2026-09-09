namespace Venly.Wallet.Helper;

/// <summary>
/// Creating the one wallet a customer gets, over HMAC.
///
/// <para>A third interface rather than a method on either of the others, and the split is the same capability
/// boundary they draw between themselves: provisioning is a LIFECYCLE act performed once when a customer
/// becomes verified, an intent is somebody's money, and maintenance is scheduled work. CustomerService holds
/// this one and neither of the others — it decides who is verified, and must never acquire the ability to
/// move a balance as a side effect of saying so.</para>
///
/// <para>The direction matters too. CustomerService calls WalletService, never the reverse: the ledger must
/// not be able to decide who is allowed an account.</para>
/// </summary>
public interface IWalletProvisioningClient
{
    /// <summary>
    /// Creates the customer's wallet, denominated in <paramref name="baseCurrency"/>.
    ///
    /// <para>**Idempotent by outcome.** A wallet that already exists comes back as
    /// <see cref="WalletProvisionOutcome.AlreadyExists"/> rather than as a failure, because that is what a
    /// retry looks like and a retry must not turn a verified customer's promotion into an error. The unique
    /// index on the customer id is what actually guarantees there is only ever one.</para>
    /// </summary>
    Task<WalletProvisionResult> ProvisionAsync(
        string customerId, string baseCurrency, CancellationToken ct = default);
}

public enum WalletProvisionOutcome
{
    Created = 0,

    /// <summary>Already provisioned. A success for every caller's purposes — see the interface remarks.</summary>
    AlreadyExists = 1,

    /// <summary>
    /// WalletService refused or could not be reached.
    ///
    /// <para>Deliberately NOT thrown. The caller is promoting a customer who has just passed verification, and
    /// an exception there would fail the promotion over a downstream that will be back in a minute — leaving
    /// a customer who verified and was told they had not. See how CustomerService handles it.</para>
    /// </summary>
    Failed = 2,
}

/// <param name="WalletId">Null unless the call created or found a wallet.</param>
/// <param name="Message">WalletService's own message on a failure, for the log that will be read later.</param>
public record WalletProvisionResult(
    WalletProvisionOutcome Outcome, string? WalletId, string? Message)
{
    public bool Provisioned => Outcome is WalletProvisionOutcome.Created or WalletProvisionOutcome.AlreadyExists;
}

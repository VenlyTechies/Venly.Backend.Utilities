namespace Venly.Corridor.Helper;

/// <summary>
/// One route, as WalletService's internal snapshot publishes it.
///
/// <para>
/// Codes only, unlike the customer-facing contract, which inlines the whole currency so an app can draw a
/// flag. The callers of THIS client are services deciding whether a corridor is real, and a name and a flag
/// URL would be weight they never read.
/// </para>
/// </summary>
/// <param name="PayoutRail">
/// The rail currently first in this corridor's PAYOUT priority order, or null where none is configured.
///
/// <para>Carried so a caller that has to price a corridor — the rate-snapshot tick — quotes on the rail the
/// payout would actually use. Without it that tick had to be told a rail by configuration, which is the
/// arrangement this whole change removed: a rate captured on one provider while payouts go out on another is
/// a series that describes nothing anybody is charged.</para>
///
/// <para>A SNAPSHOT of the order, not a reservation of it. An operator reprioritising between this read and
/// the quote means the next tick samples the new rail, which is the intended behaviour.</para>
/// </param>
public sealed record CorridorEntry(
    string Code, string BaseCurrency, string DestinationCurrency, string? PayoutRail = null);

/// <summary>Every ENABLED route, at one moment. Disabled ones are absent rather than flagged.</summary>
/// <param name="ActiveRails">
/// Every rail code currently enabled, whatever corridor it serves.
///
/// <para>For the reconciliation sweep, which has to ask EVERY provider what it holds. It used to read one
/// default rail's balances and compare the whole float against them — so the money sitting at any other
/// provider was reported as missing, and the break it raised was an artefact of the question rather than a
/// fact about the money.</para>
/// </param>
public sealed record CorridorSnapshot(
    IReadOnlyList<CorridorEntry> Corridors,
    IReadOnlyList<string> ActiveRails);

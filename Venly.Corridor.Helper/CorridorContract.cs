namespace Venly.Corridor.Helper;

/// <summary>
/// One route, as PaymentService's internal snapshot publishes it.
///
/// <para>
/// Codes only, unlike the customer-facing contract, which inlines the whole currency so an app can draw a
/// flag. The callers of THIS client are services deciding whether a corridor is real, and a name and a flag
/// URL would be weight they never read.
/// </para>
/// </summary>
public sealed record CorridorEntry(string Code, string BaseCurrency, string DestinationCurrency);

/// <summary>Every ENABLED route, at one moment. Disabled ones are absent rather than flagged.</summary>
public sealed record CorridorSnapshot(IReadOnlyList<CorridorEntry> Corridors);

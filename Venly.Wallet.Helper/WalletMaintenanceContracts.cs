namespace Venly.Wallet.Helper;

/// <summary>
/// The wire contracts for WalletService's <c>/internal/wallet/*</c> maintenance surface, shared by the service
/// that serves them and WorkflowService, which calls them on a schedule.
///
/// <para>They live here rather than in either service because a request record defined twice is a contract that
/// can drift on one side only — and these cross an HMAC boundary, where a mismatch surfaces as a
/// deserialisation failure inside a Temporal activity rather than as a compile error.</para>
/// </summary>
public sealed record ExpireIntentsResult(int Expired, long ReleasedMinorTotal);

/// <param name="ValuationRate">
/// Destination units per one SOURCE unit. Supplied by the CALLER: WalletService holds no rate feed, and a rate
/// stored there would silently change what every past snapshot meant.
/// </param>
public sealed record FxPositionValuationRequest(string Source, string Destination, decimal ValuationRate);

public sealed record SampleFxPositionRequestBody(List<FxPositionValuationRequest> Valuations);

public sealed record FxPositionSnapshotSummary(
    string CurrencyPair, decimal Exposure, decimal Cap, decimal Headroom, string State);

/// <param name="NotSampled">
/// Supported pairs no rate was supplied for. Reported rather than passed over: "we sampled and it was within
/// cap" and "we did not sample that pair" must not look the same.
/// </param>
public sealed record SampleFxPositionResultBody(
    List<FxPositionSnapshotSummary> Snapshots, List<string> NotSampled, int BreachCount);

public sealed record ProviderBalanceEntryRequest(
    string Currency,
    decimal AvailableBalance,
    decimal LockedBalance,
    decimal LedgerBalance,
    decimal? RollingReserveBalance,
    DateTime AsAt);

public sealed record IngestProviderBalancesRequestBody(
    string Source, List<ProviderBalanceEntryRequest> Balances, string? RawPayload);

public sealed record IngestProviderBalancesResultBody(
    int Ingested, int Skipped, List<string> SkippedCurrencies, List<string> SnapshotIds);

/// <param name="LegsCompared">Two today; three when a bank statement feed lands.</param>
public sealed record ReconciliationRunSummary(
    string RunId,
    string Currency,
    string Outcome,
    int BreakCount,
    decimal UnmatchedValue,
    short LegsCompared);

public sealed record SafeguardingSnapshotSummary(string Currency, decimal Total, DateTime AsAt);

public sealed record StoreRateSnapshotBody(
    string SourceCurrency,
    string DestinationCurrency,
    decimal Rate,
    decimal Fee,
    string FeeCurrency,
    string Provider,
    DateTime CapturedAt);

/// <param name="Stored">
/// False where a sample for that pair and minute already existed -- a re-fired tick or a second replica.
/// Neither is an error.
/// </param>
public sealed record StoreRateSnapshotResultBody(bool Stored, int MarginBps);

/// <param name="Fired">Notifications dispatched.</param>
/// <param name="Skipped">Alerts crossed but already told within the day.</param>
/// <param name="Failures">One message per alert whose notification could not be sent.</param>
public sealed record RateAlertRunResultBody(int Fired, int Skipped, List<string> Failures);

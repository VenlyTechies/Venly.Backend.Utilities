using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Venly.Backend.Utilities.Tests.Notification;
using Venly.Wallet.Helper;

namespace Venly.Backend.Utilities.Tests.Wallet;

/// <summary>
/// The typed HMAC client WorkflowService drives WalletService's scheduled work through.
///
/// <para>The signing string is the one <c>HmacAuthorizationFilter</c> verifies, so these tests recompute it
/// independently of the call — a signature that differed by a newline would fail every request with a 401 that
/// reads as a wrong secret rather than a wrong string.</para>
/// </summary>
public class WalletMaintenanceClientTests
{
    private static WalletMaintenanceClient NewClient(
        FakeHttpMessageHandler handler, string secret = "test-secret") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://wallet.internal") },
            Options.Create(new WalletClientOptions { HmacSecret = secret }));

    private static FakeHttpMessageHandler Responding(
        string json,
        Action<HttpRequestMessage, string?>? capture = null,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(async (request, ct) =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            capture?.Invoke(request, body);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        });

    [Fact]
    public async Task ExpireReservationsAsync_signs_the_request_and_reads_the_count()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        var handler = Responding(
            """{"responseCode":200,"responseMessage":"Successful","responseData":{"expired":7,"releasedMinorTotal":123456}}""",
            (request, body) => { captured = request; capturedBody = body; });

        var result = await NewClient(handler).ExpireReservationsAsync(200);

        Assert.Equal(7, result.Expired);
        Assert.Equal(123_456, result.ReleasedMinorTotal);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/internal/wallet/intents/expire", captured.RequestUri!.AbsolutePath);

        var timestamp = long.Parse(captured.Headers.GetValues("X-Timestamp").Single());
        var expected = WalletMaintenanceClient.ComputeSignature(
            "test-secret", timestamp, "POST", "/internal/wallet/intents/expire", capturedBody!);

        Assert.Equal(expected, captured.Headers.GetValues("X-Signature").Single());
    }

    [Fact]
    public async Task The_signing_string_is_identical_to_the_audit_client_s()
    {
        // Copied rather than reimplemented, on purpose: it is the string HmacAuthorizationFilter verifies, and
        // two implementations differing by a newline would each pass their own tests and fail in production.
        const string secret = "test-secret";
        const long timestamp = 1_800_000_000;

        Assert.Equal(
            Venly.Audit.Helper.AuditMaintenanceClient.ComputeSignature(
                secret, timestamp, "POST", "/internal/wallet/intents/expire", """{"batchSize":200}"""),
            WalletMaintenanceClient.ComputeSignature(
                secret, timestamp, "POST", "/internal/wallet/intents/expire", """{"batchSize":200}"""));
    }

    [Fact]
    public async Task The_batch_size_goes_in_the_body_as_camelCase()
    {
        // WalletService's request DTOs bind with ASP.NET's web defaults. PascalCase would bind to nothing and
        // the sweep would silently run with its default batch size.
        string? capturedBody = null;

        var handler = Responding(
            """{"responseData":{"expired":0,"releasedMinorTotal":0}}""",
            (_, body) => capturedBody = body);

        await NewClient(handler).ExpireReservationsAsync(50);

        Assert.Equal("""{"batchSize":50}""", capturedBody);
    }

    [Fact]
    public async Task RunReconciliationAsync_reads_a_run_per_currency()
    {
        var handler = Responding("""
            {"responseData":[
              {"runId":"RCR1","currency":"GBP","outcome":"Balanced","breakCount":0,"unmatchedValue":0,"legsCompared":2},
              {"runId":"RCR2","currency":"USD","outcome":"Broken","breakCount":1,"unmatchedValue":10.5,"legsCompared":2}
            ]}
            """);

        var runs = await NewClient(handler).RunReconciliationAsync(null);

        Assert.Equal(2, runs.Count);
        Assert.Equal("Broken", runs[1].Outcome);
        Assert.Equal(10.5m, runs[1].UnmatchedValue);
        Assert.Equal(2, runs[0].LegsCompared);
    }

    [Fact]
    public async Task A_null_currency_filter_is_still_sent_so_the_service_runs_them_all()
    {
        string? capturedBody = null;
        var handler = Responding("""{"responseData":[]}""", (_, body) => capturedBody = body);

        await NewClient(handler).RunReconciliationAsync(null);

        Assert.Equal("""{"currency":null}""", capturedBody);
    }

    [Fact]
    public async Task SampleFxPositionsAsync_posts_the_valuations_and_reads_the_breach_count()
    {
        string? capturedBody = null;

        var handler = Responding("""
            {"responseData":{
              "snapshots":[{"currencyPair":"GBP/NGN","exposure":0,"cap":50000,"headroom":50000,"state":"WithinCap"}],
              "notSampled":["USD/NGN"],
              "breachCount":0}}
            """, (_, body) => capturedBody = body);

        var result = await NewClient(handler).SampleFxPositionsAsync(
            new SampleFxPositionRequestBody([new FxPositionValuationRequest("GBP", "NGN", 2050m)]));

        Assert.Single(result.Snapshots);
        Assert.Equal(["USD/NGN"], result.NotSampled);
        Assert.Equal(0, result.BreachCount);
        Assert.Contains("\"valuationRate\":2050", capturedBody);
    }

    [Fact]
    public async Task IngestProviderBalancesAsync_posts_MAJOR_units_as_the_provider_reports_them()
    {
        // The ledger speaks minor and the provider speaks major. Conversion happens once, inside WalletService,
        // so this client must not pre-convert -- doing so would multiply every figure by a hundred.
        string? capturedBody = null;

        var handler = Responding(
            """{"responseData":{"ingested":1,"skipped":0,"skippedCurrencies":[],"snapshotIds":["EBS1"]}}""",
            (_, body) => capturedBody = body);

        var result = await NewClient(handler).IngestProviderBalancesAsync(
            new IngestProviderBalancesRequestBody("fincra",
                [new ProviderBalanceEntryRequest("GBP", 1234.56m, 0m, 1234.56m, null,
                    new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc))],
                """{"raw":true}"""));

        Assert.Equal(1, result.Ingested);
        Assert.Contains("\"availableBalance\":1234.56", capturedBody);
    }

    [Fact]
    public async Task GenerateSafeguardingSnapshotsAsync_reads_one_total_per_currency()
    {
        var handler = Responding("""
            {"responseData":[
              {"currency":"GBP","total":5000.00,"asAt":"2026-08-27T00:05:00Z"},
              {"currency":"USD","total":0,"asAt":"2026-08-27T00:05:00Z"},
              {"currency":"NGN","total":0,"asAt":"2026-08-27T00:05:00Z"}]}
            """);

        var snapshots = await NewClient(handler).GenerateSafeguardingSnapshotsAsync();

        Assert.Equal(3, snapshots.Count);
        Assert.Equal(5000.00m, snapshots[0].Total);
    }

    [Fact]
    public async Task A_missing_secret_throws_rather_than_signing_with_an_empty_key()
    {
        // Signing with an empty key produces a valid-LOOKING signature the server rejects, so the failure would
        // surface as a 401 from inside a Temporal activity with nothing pointing at a credential.
        var client = new WalletMaintenanceClient(
            new HttpClient(Responding("{}")) { BaseAddress = new Uri("https://wallet.internal") },
            Options.Create(new WalletClientOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ExpireReservationsAsync(200));

        Assert.Contains("HmacSecret", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_non_success_response_THROWS_so_Temporal_retries(HttpStatusCode status)
    {
        // Swallowing it would let a reconciliation "succeed" having compared nothing, and the schedule would
        // report green while the break it should have raised went unnoticed.
        var handler = Responding("""{"responseMessage":"nope"}""", status: status);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => NewClient(handler).RunReconciliationAsync(null));
    }

    [Fact]
    public async Task A_success_with_NO_responseData_throws_too()
    {
        // A 200 with an empty envelope means the service answered without doing the work. Treating it as
        // success would record maintenance that never happened.
        var handler = Responding("""{"responseCode":200,"responseMessage":"Successful"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewClient(handler).ExpireReservationsAsync(200));

        Assert.Contains("no responseData", ex.Message);
    }

    [Fact]
    public async Task ReconcileConversionsAsync_posts_the_window_and_reads_the_tally()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        var handler = Responding(
            """{"responseCode":200,"responseMessage":"Successful","responseData":{"examined":3,"completed":1,"failed":1,"stillProcessing":1,"failures":[]}}""",
            (request, body) => { captured = request; capturedBody = body; });

        var result = await NewClient(handler).ReconcileConversionsAsync(new ReconcileConversionsRequestBody(5, 50));

        Assert.Equal(new[] { 3, 1, 1, 1 }, new[] { result.Examined, result.Completed, result.Failed, result.StillProcessing });
        Assert.Equal("/internal/wallet/conversions/reconcile", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("\"minimumAgeMinutes\":5", capturedBody);
        Assert.Contains("\"batchSize\":50", capturedBody);
    }

    [Fact]
    public async Task ReconcileTransfersAsync_posts_the_window_and_reads_the_tally()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        var handler = Responding(
            """{"responseCode":200,"responseMessage":"Successful","responseData":{"examined":2,"completed":1,"failed":0,"stillProcessing":1,"failures":[]}}""",
            (request, body) => { captured = request; capturedBody = body; });

        var result = await NewClient(handler).ReconcileTransfersAsync(new ReconcileTransfersRequestBody(10, 50));

        Assert.Equal(new[] { 2, 1, 0, 1 }, new[] { result.Examined, result.Completed, result.Failed, result.StillProcessing });
        Assert.Equal("/internal/wallet/transfers/reconcile", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("\"minimumAgeMinutes\":10", capturedBody);
    }

    [Fact]
    public async Task RunDueAllowancesAsync_posts_the_batch_and_reads_the_tally()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        var handler = Responding(
            """{"responseCode":200,"responseMessage":"Successful","responseData":{"examined":3,"instructed":2,"retrying":1,"missed":0,"failures":[]}}""",
            (request, body) => { captured = request; capturedBody = body; });

        var result = await NewClient(handler).RunDueAllowancesAsync(new RunDueAllowancesRequestBody(100));

        Assert.Equal(new[] { 3, 2, 1, 0 }, new[] { result.Examined, result.Instructed, result.Retrying, result.Missed });
        Assert.Equal("/internal/wallet/allowances/run-due", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("\"batchSize\":100", capturedBody);
    }

    [Fact]
    public void Every_path_is_under_internal_wallet()
    {
        // None of this surface is published at the gateway. A path that drifted onto /api would be a scheduled
        // job calling a customer route.
        foreach (var path in new[]
        {
            WalletMaintenanceClient.ExpirePath,
            WalletMaintenanceClient.ReconciliationRunPath,
            WalletMaintenanceClient.ProviderBalancesPath,
            WalletMaintenanceClient.SafeguardingPath,
            WalletMaintenanceClient.FxPositionPath,
            WalletMaintenanceClient.ReconcileConversionsPath,
            WalletMaintenanceClient.ReconcileTransfersPath,
            WalletMaintenanceClient.RunDueAllowancesPath,
        })
        {
            Assert.StartsWith("/internal/wallet/", path, StringComparison.Ordinal);
        }
    }
}

using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Venly.Backend.Utilities.Tests.Notification;
using Venly.Rails.Helper;

namespace Venly.Backend.Utilities.Tests.Rails;

/// <summary>
/// The client WalletService's reconciliation PULLS provider balances through. Read-only by design: there is no
/// payout here, because a ledger that could instruct a payment could move money without an intent.
/// </summary>
public class RailsClientTests
{
    private static RailsClient NewClient(FakeHttpMessageHandler handler, string secret = "test-secret") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://payment.internal") },
            Options.Create(new RailsClientOptions { HmacSecret = secret }));

    private static FakeHttpMessageHandler Responding(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        Action<HttpRequestMessage, string?>? capture = null) =>
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
    public async Task Balances_come_back_in_MINOR_units_with_the_provider_named()
    {
        // Minor units because that is what the ledger speaks and PaymentService has already converted at the
        // provider boundary -- a contract in major units would put the conversion in two places.
        var handler = Responding("""
            {"responseCode":200,"responseData":{"provider":"stub","balances":[
              {"currency":"GBP","availableMinor":123456,"lockedMinor":1000,"ledgerMinor":124456,
               "rollingReserveMinor":null,"asAt":"2026-08-27T12:00:00Z"}]}}
            """);

        var result = await NewClient(handler).GetBalancesAsync("fincra");

        Assert.Equal("stub", result.Provider);
        var gbp = Assert.Single(result.Balances);
        Assert.Equal(123_456, gbp.AvailableMinor);
        Assert.Equal(124_456, gbp.LedgerMinor);
        Assert.Null(gbp.RollingReserveMinor);
    }

    [Fact]
    public async Task The_provider_name_is_carried_so_a_stub_run_is_distinguishable()
    {
        // It lands in external_balance_snapshot.Source, so a reconciliation run against the stub can be told
        // apart from one against the real provider.
        var handler = Responding(
            """{"responseData":{"provider":"fincra","balances":[]}}""");

        Assert.Equal("fincra", (await NewClient(handler).GetBalancesAsync("fincra")).Provider);
    }

    [Fact]
    public async Task A_balances_read_is_a_signed_GET()
    {
        HttpRequestMessage? captured = null;
        var handler = Responding(
            """{"responseData":{"provider":"stub","balances":[]}}""",
            capture: (request, _) => captured = request);

        await NewClient(handler).GetBalancesAsync("fincra");

        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/internal/payment/rails/balances", captured.RequestUri!.AbsolutePath);

        var timestamp = long.Parse(captured.Headers.GetValues("X-Timestamp").Single());
        var expected = RailsClient.ComputeSignature(
            "test-secret", timestamp, "GET", RailsClient.BalancesPath, string.Empty);

        Assert.Equal(expected, captured.Headers.GetValues("X-Signature").Single());
    }

    [Fact]
    public void The_signing_string_is_identical_to_every_other_helper_s()
    {
        // It is what HmacAuthorizationFilter verifies. Two implementations differing by a newline would each
        // pass their own tests and fail in production.
        const string secret = "test-secret";
        const long timestamp = 1_800_000_000;

        Assert.Equal(
            Venly.Audit.Helper.AuditMaintenanceClient.ComputeSignature(
                secret, timestamp, "GET", RailsClient.BalancesPath, string.Empty),
            RailsClient.ComputeSignature(
                secret, timestamp, "GET", RailsClient.BalancesPath, string.Empty));
    }

    [Fact]
    public async Task A_filtered_read_signs_the_path_WITHOUT_the_query()
    {
        // HmacAuthorizationFilter verifies request.Path.Value, which excludes the query string, and so does every
        // other signer (the gateway signs AbsolutePath). Signing the path AND query made every filtered read -- the
        // bank list, account resolution, the statement -- a 401 in production, while a test that only compared
        // the client with itself stayed green. So this compares with what the FILTER computes.
        HttpRequestMessage? captured = null;
        var handler = Responding("""{"responseData":[]}""", capture: (request, _) => captured = request);

        await NewClient(handler).GetStatementAsync(
            "fincra", "GBP", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 27));

        var uri = captured!.RequestUri!;
        var timestamp = long.Parse(captured.Headers.GetValues("X-Timestamp").Single());

        Assert.Contains("currency=GBP", uri.Query);
        Assert.Equal(
            Venly.Backend.Common.Hmac.HmacSignature.Compute("test-secret", timestamp, "GET", uri.AbsolutePath, string.Empty),
            captured.Headers.GetValues("X-Signature").Single());
    }

    [Fact]
    public async Task Banks_come_back_as_a_list()
    {
        var handler = Responding("""
            {"responseData":[{"code":"000013","name":"GTBank","type":"nuban"}]}
            """);

        Assert.Equal("000013", Assert.Single(await NewClient(handler).GetBanksAsync("fincra", "NGN", "NG")).Code);
    }

    [Fact]
    public async Task Resolving_an_account_is_a_signed_POST()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        var handler = Responding("""
            {"responseData":{"accountNumber":"0123456789","bankCode":"000013",
             "accountHolderName":"A N OTHER"}}
            """,
            capture: (request, body) => { captured = request; capturedBody = body; });

        var name = await NewClient(handler).ResolveAccountAsync("fincra", "0123456789", "000013");

        Assert.Equal("A N OTHER", name.AccountHolderName);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("\"accountNumber\":\"0123456789\"", capturedBody);

        var timestamp = long.Parse(captured.Headers.GetValues("X-Timestamp").Single());
        Assert.Equal(
            RailsClient.ComputeSignature(
                "test-secret", timestamp, "POST", RailsClient.ResolveAccountPath, capturedBody!),
            captured.Headers.GetValues("X-Signature").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_non_success_THROWS_so_the_activity_retries(HttpStatusCode status)
    {
        // A reconciliation that "succeeded" having fetched no balances would report Incomplete on a green
        // schedule, while the break it should have raised went unnoticed.
        var handler = Responding("""{"responseMessage":"nope"}""", status);

        await Assert.ThrowsAsync<HttpRequestException>(() => NewClient(handler).GetBalancesAsync("fincra"));
    }

    [Fact]
    public async Task A_200_with_NO_responseData_throws_too()
    {
        var handler = Responding("""{"responseCode":200,"responseMessage":"Successful"}""");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewClient(handler).GetBalancesAsync("fincra"));

        Assert.Contains("never fetched", ex.Message);
    }

    [Fact]
    public async Task A_missing_secret_throws_rather_than_signing_with_an_empty_key()
    {
        var client = new RailsClient(
            new HttpClient(Responding("{}")) { BaseAddress = new Uri("https://payment.internal") },
            Options.Create(new RailsClientOptions()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetBalancesAsync("fincra"));

        Assert.Contains("HmacSecret", ex.Message);
    }

    /// <summary>
    /// Every write on this client carries the intent it settles.
    ///
    /// <para>This asserted that no payout method EXISTED: "WalletService is the ledger, and a ledger that
    /// could instruct a payment could move money without an intent." That was the right pin while the
    /// instruction flowed the other way. WalletService owns transfers now and must instruct, so the missing
    /// method can no longer carry the rule — but the rule itself is unchanged, and this is it.</para>
    ///
    /// <para>The two exemptions are named rather than inferred, so a new write that forgets an intent id fails
    /// here instead of passing on a technicality.</para>
    /// </summary>
    [Fact]
    public void Every_write_on_this_client_names_the_intent_it_settles()
    {
        // None of these takes money OUT, which is what the rule is about. A requery asks what already
        // happened -- and is the right response to a timeout, which may have been acted on with the response
        // lost. A quote prices a route before any intent exists, so requiring one would make the first step of
        // a transfer impossible. And a CHECKOUT is inbound: it opens a page for money to come in, reserves
        // nothing and credits nothing, with the intent created only when the charge.successful webhook says
        // the money arrived -- so requiring one would demand an intent for a payment that may never be made,
        // and funding could not satisfy it at all.
        string[] movesNothing =
        [
            nameof(IRailsClient.RequeryPayoutAsync),
            nameof(IRailsClient.GenerateQuoteAsync),
            nameof(IRailsClient.CreateCheckoutAsync),
            nameof(IRailsClient.GetBalancesAsync),
            nameof(IRailsClient.GetStatementAsync),
            nameof(IRailsClient.GetBanksAsync),
            nameof(IRailsClient.ResolveAccountAsync),
            // Which rails exist and what they serve: a read, and it takes no body at all.
            nameof(IRailsClient.GetRegistryAsync),
        ];

        var writes = typeof(IRailsClient).GetMethods()
            .Where(m => !movesNothing.Contains(m.Name))
            .ToList();

        Assert.NotEmpty(writes);

        foreach (var write in writes)
        {
            var body = write.GetParameters().First().ParameterType;

            Assert.True(
                body.GetProperty("IntentId") is not null,
                $"{write.Name} moves money but its body has no IntentId. PaymentService refuses a rails write "
                + "without one: a payout with no intent is money leaving the system with nothing in the ledger "
                + "reserving it.");
        }
    }

    [Fact]
    public void Every_path_is_under_internal_payment()
    {
        foreach (var path in new[]
        {
            RailsClient.BalancesPath, RailsClient.StatementPath,
            RailsClient.BanksPath, RailsClient.ResolveAccountPath,
            RailsClient.QuotesPath, RailsClient.PayoutsPath,
            RailsClient.ConversionsPath, RailsClient.CheckoutPath,
            RailsClient.RequeryPath("fincra", "REF-1"),
        })
        {
            Assert.StartsWith("/internal/payment/", path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_timeout_is_LONGER_than_the_wallet_client_s()
    {
        // Every call behind this one reaches an external provider, and PaymentService allows that provider
        // thirty seconds of its own -- so a shorter timeout here would abandon a request still in flight and
        // report a failure the provider never had.
        Assert.True(new RailsClientOptions().TimeoutSeconds
                    > new Venly.Wallet.Helper.WalletClientOptions().TimeoutSeconds);
    }
}

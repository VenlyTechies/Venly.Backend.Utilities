using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Venly.Backend.Common.Hmac;
using Venly.Backend.Utilities.Tests.Notification;
using Venly.Identity.Helper;
namespace Venly.Backend.Utilities.Tests.Identity;
public class IdentityClientTests
{
    private static IdentityClient NewClient(FakeHttpMessageHandler handler, string secret = "id-secret") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://identityservice:8080") },
            Options.Create(new IdentityClientOptions { HmacSecret = secret }), NullLogger<IdentityClient>.Instance);
    private static FakeHttpMessageHandler Replying(HttpStatusCode code, string json, Action<HttpRequestMessage, string>? capture = null) =>
        new(async (request, ct) =>
        {
            capture?.Invoke(request, await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(code) { Content = new StringContent(json) };
        });
    [Fact]
    public async Task A_check_is_signed_over_the_exact_path_and_body_with_a_nonce()
    {
        HttpRequestMessage? sent = null; string? body = null;
        var client = NewClient(Replying(HttpStatusCode.OK, """{"responseCode":200,"responseData":{"allowed":true}}""",
            (r, b) => { sent = r; body = b; }));
        var decision = await client.CheckPermissionAsync("U1", "venue.edit", "venue", "VEN1", CancellationToken.None);
        Assert.Equal(PermissionDecision.Allowed, decision);
        Assert.Equal("/internal/permissions/check", sent!.RequestUri!.AbsolutePath);
        Assert.Contains("\"scopeType\":\"venue\"", body);
        var timestamp = long.Parse(sent.Headers.GetValues("X-Timestamp").Single());
        var nonce = sent.Headers.GetValues("X-Nonce").Single();
        Assert.Equal(
            HmacSignature.Compute("id-secret", timestamp, "POST", "/internal/permissions/check", body!, nonce),
            sent.Headers.GetValues("X-Signature").Single());
    }
    [Fact]
    public async Task A_denial_is_Denied()
    {
        var client = NewClient(Replying(HttpStatusCode.OK, """{"responseData":{"allowed":false}}"""));
        Assert.Equal(PermissionDecision.Denied, await client.CheckPermissionAsync("U", "p", "venue", "V", CancellationToken.None));
    }
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.Unauthorized, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"responseData":null}""")]
    [InlineData(HttpStatusCode.OK, """{"responseData":{"allowed":"yes"}}""")]
    public async Task Anything_unexpected_is_Unavailable_never_Denied_and_never_Allowed(HttpStatusCode code, string json)
    {
        var client = NewClient(Replying(code, json));
        Assert.Equal(PermissionDecision.Unavailable, await client.CheckPermissionAsync("U", "p", "venue", "V", CancellationToken.None));
    }
    [Fact]
    public async Task An_unreachable_service_is_Unavailable()
    {
        var client = NewClient(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("refused")));
        Assert.Equal(PermissionDecision.Unavailable, await client.CheckPermissionAsync("U", "p", "venue", "V", CancellationToken.None));
    }
    [Fact]
    public async Task A_cancelled_caller_is_not_reported_as_an_outage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = NewClient(new FakeHttpMessageHandler((_, ct) => throw new TaskCanceledException()));
        await Assert.ThrowsAsync<TaskCanceledException>(() => client.CheckPermissionAsync("U", "p", "venue", "V", cts.Token));
    }
    [Fact]
    public async Task Creating_an_organisation_returns_its_id_and_null_on_refusal()
    {
        var ok = NewClient(Replying(HttpStatusCode.OK, """{"responseData":{"organisationId":"ORG1"}}"""));
        var request = new CreateOrganisationRequest("U1", "venue", "Hall", "venue:V1", "venue_owner", "venue", "V1");
        Assert.Equal("ORG1", (await ok.CreateOrganisationAsync(request, CancellationToken.None))!.OrganisationId);
        var conflict = NewClient(Replying(HttpStatusCode.Conflict, "{}"));
        Assert.Null(await conflict.CreateOrganisationAsync(request, CancellationToken.None));
    }
    [Fact]
    public async Task A_missing_secret_fails_loudly_rather_than_sending_unsigned()
    {
        var client = NewClient(Replying(HttpStatusCode.OK, "{}"), secret: "");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CheckPermissionAsync("U", "p", "venue", "V", CancellationToken.None));
    }
    [Fact]
    public async Task Names_are_looked_up_in_one_signed_call_and_a_failure_is_an_empty_answer_not_an_error()
    {
        string? body = null;
        var ok = NewClient(Replying(HttpStatusCode.OK,
            """{"responseData":{"users":[{"userId":"U1","fullName":"Ada"},{"userId":"U2","fullName":null}]}}""", (_, b) => body = b));
        var names = await ok.LookupNamesAsync(["U1", "U2", "U1", " "], CancellationToken.None);
        Assert.Equal("Ada", names["U1"]);
        Assert.Null(names["U2"]);
        Assert.Equal(2, names.Count);
        Assert.Contains("\"userIds\":[\"U1\",\"U2\"]", body);            // de-duplicated, blanks dropped
        Assert.Empty(await NewClient(Replying(HttpStatusCode.InternalServerError, "{}")).LookupNamesAsync(["U1"], CancellationToken.None));
        Assert.Empty(await NewClient(Replying(HttpStatusCode.OK, "not json")).LookupNamesAsync(["U1"], CancellationToken.None));
        Assert.Empty(await NewClient(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException())).LookupNamesAsync(["U1"], CancellationToken.None));
    }
    [Fact]
    public async Task Looking_up_nobody_makes_no_call()
    {
        var called = false;
        var client = NewClient(new FakeHttpMessageHandler((_, _) => { called = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }));
        Assert.Empty(await client.LookupNamesAsync([], CancellationToken.None));
        Assert.False(called);
    }
}

using System.Net;
using Microsoft.Extensions.Options;
using Venly.Backend.Utilities.Tests.Notification;
using Venly.Customer.Helper;
using Venly.Messaging.Events;
using Venly.Notification.Helper;

namespace Venly.Backend.Utilities.Tests.Customer;

public class CustomerClientTests
{
    private static CustomerClient NewClient(FakeHttpMessageHandler handler, string secret = "test-secret") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://customerservice:8080") },
            Options.Create(new CustomerClientOptions { HmacSecret = secret }));

    private static CustomerNotification Sent() => new(
        "ALLOWANCE_SENT", [NotificationChannel.Push, NotificationChannel.InApp],
        new Dictionary<string, string> { ["amount"] = "£100.00", ["nickname"] = "Mum" });

    [Fact]
    public async Task NotifyAsync_signs_the_exact_path_and_posts_string_enums()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new FakeHttpMessageHandler(async (request, ct) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"responseCode":200,"responseData":{"sent":["Push","InApp"],"skipped":[],"failed":[]}}"""),
            };
        });

        var result = await NewClient(handler).NotifyAsync("CUS 1/2", Sent());

        Assert.Equal(["Push", "InApp"], result.Sent);
        Assert.Equal("/internal/customers/CUS%201%2F2/notifications", captured!.RequestUri!.AbsolutePath);
        Assert.Contains("\"channels\":[\"Push\",\"InApp\"]", body);
        Assert.Contains("\"templateCode\":\"ALLOWANCE_SENT\"", body);

        var timestamp = long.Parse(captured.Headers.GetValues("X-Timestamp").Single());
        Assert.Equal(
            NotificationClient.ComputeSignature("test-secret", timestamp, "POST", CustomerClient.NotifyPath("CUS 1/2"), body!),
            captured.Headers.GetValues("X-Signature").Single());
    }

    [Fact]
    public async Task NotifyAsync_throws_on_a_bad_gateway_so_the_caller_retries()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("{}") }));

        await Assert.ThrowsAsync<HttpRequestException>(() => NewClient(handler).NotifyAsync("CUS1", Sent()));
    }

    [Fact]
    public async Task NotifyAsync_throws_when_the_secret_is_not_configured()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewClient(handler, secret: "").NotifyAsync("CUS1", Sent()));
    }
}

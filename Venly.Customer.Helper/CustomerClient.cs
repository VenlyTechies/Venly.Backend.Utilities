using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Venly.Notification.Helper;

namespace Venly.Customer.Helper;

/// <summary>
/// HMAC-signed calls into CustomerService's <c>/internal</c> surface, in the same scheme every Venly client uses
/// (<see cref="NotificationClient.ComputeSignature"/>). The signed path is the exact escaped request path,
/// because that is what the callee recomputes.
/// </summary>
public sealed class CustomerClient(HttpClient httpClient, IOptions<CustomerClientOptions> options) : ICustomerClient
{
    /// <summary>CustomerService reads enums as strings; camelCase like every service envelope.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string NotifyPath(string customerId) =>
        $"/internal/customers/{Uri.EscapeDataString(customerId)}/notifications";

    public async Task<CustomerNotificationResult> NotifyAsync(
        string customerId, CustomerNotification notification, CancellationToken ct = default)
    {
        var secret = options.Value.HmacSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("CustomerClient:HmacSecret is not configured.");

        var path = NotifyPath(customerId);
        var body = JsonSerializer.Serialize(notification, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Signature", NotificationClient.ComputeSignature(secret, timestamp, "POST", path, body));

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await JsonSerializer.DeserializeAsync<Envelope>(
            await response.Content.ReadAsStreamAsync(ct), Json, ct);

        return envelope?.ResponseData ?? new CustomerNotificationResult([], [], []);
    }

    private sealed record Envelope(CustomerNotificationResult? ResponseData);
}

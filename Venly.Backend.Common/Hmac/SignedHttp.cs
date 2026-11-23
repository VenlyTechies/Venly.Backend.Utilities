using System.Text;
namespace Venly.Backend.Common.Hmac;
/// <summary>
/// Sends one HMAC-signed request to another Venly service, in the scheme every service-to-service call uses: the signature
/// covers the timestamp, a fresh nonce, the method, the PATH (never the query string, which the receiver does not sign) and
/// a hash of the body. One implementation, so a typed client cannot get the path or the nonce subtly different.
/// </summary>
public static class SignedHttp
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpMethod method, string path, string? jsonBody, string secret, CancellationToken ct, string query = "")
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("The HMAC secret for this client is not configured.");
        var body = jsonBody ?? string.Empty;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(method, path + query);
        if (jsonBody is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", HmacSignature.Compute(secret, timestamp, method.Method.ToUpperInvariant(), path, body, nonce));
        // ResponseHeadersRead is not used: every caller reads the whole body, and buffering lets the request be disposed here.
        return await http.SendAsync(request, ct);
    }
}

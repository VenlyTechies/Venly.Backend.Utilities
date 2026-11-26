using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Venly.Backend.Common.Hmac;
namespace Venly.Identity.Helper;
public sealed class IdentityClient(
    HttpClient http, IOptions<IdentityClientOptions> options, ILogger<IdentityClient> logger) : IIdentityClient
{
    private const string CheckPath = "/internal/permissions/check";
    private const string OrganisationsPath = "/internal/organisations";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
    public async Task<PermissionDecision> CheckPermissionAsync(
        string userId, string permission, string scopeType, string? scopeId, CancellationToken ct)
    {
        try
        {
            var body = await PostAsync(CheckPath, new { userId, permission, scopeType, scopeId }, ct);
            if (body is null)
                return PermissionDecision.Unavailable;
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("responseData", out var data)
                   && data.ValueKind == JsonValueKind.Object
                   && data.TryGetProperty("allowed", out var allowed)
                   && allowed.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? (allowed.GetBoolean() ? PermissionDecision.Allowed : PermissionDecision.Denied)
                : PermissionDecision.Unavailable;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Not rethrown: an unreachable IdentityService is an outage the caller maps to 503. Cancellation by
            // the caller's own token is not an outage, so it is left to propagate.
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Permission check against IdentityService failed.");
            return PermissionDecision.Unavailable;
        }
    }
    public async Task<OrganisationCreated?> CreateOrganisationAsync(CreateOrganisationRequest request, CancellationToken ct)
    {
        try
        {
            var body = await PostAsync(OrganisationsPath, request, ct);
            if (body is null)
                return null;
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("responseData", out var data)
                   && data.ValueKind == JsonValueKind.Object
                   && data.TryGetProperty("organisationId", out var id)
                   && id.ValueKind == JsonValueKind.String
                ? new OrganisationCreated(id.GetString()!)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Creating an organisation at IdentityService failed.");
            return null;
        }
    }
    public async Task<IReadOnlyDictionary<string, string?>> LookupNamesAsync(IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Take(100).ToList();
        if (ids.Count == 0)
            return new Dictionary<string, string?>();
        try
        {
            var body = await PostAsync("/internal/users/lookup", new { userIds = ids }, ct);
            if (body is null)
                return new Dictionary<string, string?>();
            using var doc = JsonDocument.Parse(body);
            var result = new Dictionary<string, string?>();
            if (doc.RootElement.TryGetProperty("responseData", out var data) && data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
                foreach (var u in users.EnumerateArray())
                    if (u.TryGetProperty("userId", out var id) && id.ValueKind == JsonValueKind.String)
                        result[id.GetString()!] = u.TryGetProperty("fullName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Looking up names at IdentityService failed.");
            return new Dictionary<string, string?>();
        }
    }
    public async Task<IReadOnlyList<VenueRole>?> ListVenueRolesAsync(string userId, CancellationToken ct)
    {
        try
        {
            var body = await PostAsync("/internal/users/venue-roles", new { userId }, ct);
            if (body is null)
                return null;
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("responseData", out var data) || !data.TryGetProperty("venues", out var venues) || venues.ValueKind != JsonValueKind.Array)
                return null;
            var result = new List<VenueRole>();
            foreach (var v in venues.EnumerateArray())
                if (v.TryGetProperty("venueId", out var id) && v.TryGetProperty("role", out var role) && id.GetString() is { } i && role.GetString() is { } r)
                    result.Add(new VenueRole(i, r));
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Listing venue roles at IdentityService failed.");
            return null;
        }
    }
    public async Task<string?> GetBillingEmailAsync(string userId, CancellationToken ct)
    {
        try
        {
            var body = await PostAsync("/internal/users/billing-contact", new { userId }, ct);
            if (body is null)
                return null;
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("responseData", out var data) && data.ValueKind == JsonValueKind.Object
                   && data.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
                ? email.GetString()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning(ex, "Looking up a billing email at IdentityService failed.");
            return null;
        }
    }
    /// <summary>Signs and posts. Null on any non-2xx, which every caller treats as "no usable answer".</summary>
    private async Task<string?> PostAsync(string path, object payload, CancellationToken ct)
    {
        using var response = await SignedHttp.SendAsync(http, HttpMethod.Post, path, JsonSerializer.Serialize(payload, Json), options.Value.HmacSecret, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("IdentityService answered {Status} to POST {Path}.", (int)response.StatusCode, path);
            return null;
        }
        return await response.Content.ReadAsStringAsync(ct);
    }
}

using System.Text.Json;

namespace Venly.Audit.Helper;

/// <inheritdoc cref="IAuditScope"/>
public sealed class AuditScope : IAuditScope
{
    public string? EntityId { get; private set; }
    public string? BeforeValue { get; private set; }
    public string? AfterValue { get; private set; }
    public string? Rationale { get; private set; }
    public string? ChangeRequestId { get; private set; }
    public string? EmergencyAccessId { get; private set; }
    public bool Suppressed { get; private set; }

    public string? TenantId { get; private set; }

    public void SetEntityId(string? entityId) => EntityId = entityId;

    public void SetTenantId(string? tenantId) => TenantId = tenantId;

    public void SetBefore(object? value) => BeforeValue = ToJson(value);

    public void SetAfter(object? value) => AfterValue = ToJson(value);

    public void SetRationale(string? rationale) => Rationale = rationale;

    public void SetChangeRequestId(string? changeRequestId) => ChangeRequestId = changeRequestId;

    public void SetEmergencyAccessId(string? emergencyAccessId) => EmergencyAccessId = emergencyAccessId;

    public void Suppress() => Suppressed = true;

    /// <summary>
    /// A string that already parses as JSON is passed through untouched; anything else is serialised. Without
    /// the passthrough a handler that has already built its own JSON — which is the common case for a diff —
    /// would get a jsonb column holding a quoted string containing JSON, and every query against it would
    /// need a second parse.
    /// </summary>
    private static string? ToJson(object? value)
    {
        if (value is null)
            return null;

        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                using var _ = JsonDocument.Parse(text);
                return text;
            }
            catch (JsonException)
            {
                return JsonSerializer.Serialize(text);
            }
        }

        return JsonSerializer.Serialize(value, KafkaAuditPublisher.JsonOptions);
    }
}

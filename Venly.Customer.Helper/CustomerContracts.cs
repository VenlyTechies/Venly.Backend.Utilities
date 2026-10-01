using Venly.Messaging.Events;

namespace Venly.Customer.Helper;

/// <param name="TemplateCode">A NotificationService template, seeded on every channel named.</param>
/// <param name="Channels">CustomerService resolves each to the customer's destinations; the caller holds none.</param>
/// <param name="Fields">The template's placeholders, already formatted.</param>
public sealed record CustomerNotification(
    string TemplateCode,
    IReadOnlyList<NotificationChannel> Channels,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>Per destination: what was sent, what had nowhere to go, what failed.</summary>
public sealed record CustomerNotificationResult(List<string> Sent, List<string> Skipped, List<string> Failed);

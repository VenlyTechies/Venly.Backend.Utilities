namespace Venly.Messaging.Events;

public enum NotificationChannel
{
    Email,
    Sms,
    Push,

    /// <summary>
    /// The in-app inbox — the bell, the dropdown and the notifications page.
    ///
    /// <para>
    /// A CHANNEL rather than a separate feed, and that is the load-bearing decision: the inbox then inherits
    /// templates, categories, the customer's channel preferences, the delivery log and the provider
    /// analytics that already exist, and "which notifications appear in the app" is answered the same way as
    /// "which arrive by SMS" — by a template variant existing. A parallel inbox written at dispatch time
    /// would need a second, implicit rule for the same question.
    /// </para>
    /// <para>
    /// Its "provider" writes a <c>CustomerNotification</c> row instead of calling a vendor, and its
    /// RECIPIENT is the customer id: the address of an in-app notification is the customer themselves.
    /// </para>
    /// <para>
    /// APPENDED, never inserted. Email/Sms/Push keep ordinals 0/1/2, which the Kafka contract and every
    /// message already on the wire were written against.
    /// </para>
    /// </summary>
    InApp,
}

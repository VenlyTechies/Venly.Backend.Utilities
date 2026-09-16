namespace Venly.Corridor.Helper;

public sealed class CorridorClientOptions
{
    public const string SectionName = "CorridorClient";

    /// <summary>WalletService's base address. Service-to-service, so it does not go through the gateway.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Must equal WalletService's <c>HmacSettings:Secret</c>.</summary>
    public string HmacSecret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// How long a fetched snapshot is served before another fetch is attempted.
    ///
    /// <para>
    /// Five minutes, far longer than the flag client's thirty seconds, because the two answer different
    /// questions. A flag is changed to steer a rollout in progress and the console promises it takes effect
    /// within half a minute; a corridor is opened or closed as a commercial decision, and nobody expects
    /// either to reach every service instantly. The cost of the longer window is that a corridor closed in
    /// the back office stays selectable at registration for a few minutes, which is a far smaller problem
    /// than putting a PaymentService round trip on the registration path every time.
    /// </para>
    /// </summary>
    public int CacheSeconds { get; set; } = 300;
}

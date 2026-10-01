namespace Venly.Customer.Helper;

/// <summary>Where CustomerService's internal surface is, and the secret it verifies callers with.</summary>
public sealed class CustomerClientOptions
{
    public const string SectionName = "CustomerClient";

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>CustomerService's OWN <c>HmacSettings:Secret</c>: the callee verifies.</summary>
    public string HmacSecret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;
}

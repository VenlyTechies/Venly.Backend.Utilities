namespace Venly.Identity.Helper;
public class IdentityClientOptions
{
    public const string SectionName = "IdentityClient";
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Must equal Eventspaces IdentityService's own HmacSettings:Secret. A caller signs with the TARGET's secret.</summary>
    public string HmacSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 5;
}

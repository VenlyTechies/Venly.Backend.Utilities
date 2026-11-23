namespace Venly.Marketplace.Helper;
public class MarketplaceClientOptions
{
    public const string SectionName = "MarketplaceClient";
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Must equal MarketplaceService's own HmacSettings:Secret. A caller signs with the TARGET's secret.</summary>
    public string HmacSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}

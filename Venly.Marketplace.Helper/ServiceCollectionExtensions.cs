using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace Venly.Marketplace.Helper;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMarketplaceClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MarketplaceClientOptions>(configuration.GetSection(MarketplaceClientOptions.SectionName));
        services.AddHttpClient<IMarketplaceClient, MarketplaceClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<MarketplaceClientOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.BaseUrl))
                throw new InvalidOperationException("MarketplaceClient:BaseUrl is not configured.");
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });
        return services;
    }
}

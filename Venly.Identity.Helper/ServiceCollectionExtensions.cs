using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace Venly.Identity.Helper;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentityClientOptions>(configuration.GetSection(IdentityClientOptions.SectionName));
        services.AddHttpClient<IIdentityClient, IdentityClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<IdentityClientOptions>>().Value;
            if (string.IsNullOrWhiteSpace(opts.BaseUrl))
                throw new InvalidOperationException("IdentityClient:BaseUrl is not configured.");
            client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });
        services.AddScoped<IVenueGuard, VenueGuard>();
        return services;
    }
}

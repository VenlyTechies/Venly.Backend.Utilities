using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Venly.Customer.Helper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomerClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CustomerClientOptions>(configuration.GetSection(CustomerClientOptions.SectionName));

        services.AddHttpClient<ICustomerClient, CustomerClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<CustomerClientOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Venly.Corridor.Helper;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICorridorClient"/> as a SINGLETON over a NAMED HttpClient.
    ///
    /// <para>
    /// Deliberately not <c>AddHttpClient&lt;ICorridorClient, CorridorClient&gt;</c>, which registers the
    /// implementation as TRANSIENT — every caller would get an empty cache and refetch the snapshot on every
    /// lookup, which on this client means a PaymentService round trip per registration.
    /// </para>
    /// </summary>
    public static IServiceCollection AddCorridorClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CorridorClientOptions>(
            configuration.GetSection(CorridorClientOptions.SectionName));

        services.AddHttpClient(nameof(CorridorClient), (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<CorridorClientOptions>>().Value;
                if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                    client.BaseAddress = new Uri(opts.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddSingleton<ICorridorClient>(sp =>
            new CorridorClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(CorridorClient)),
                sp.GetRequiredService<IOptions<CorridorClientOptions>>(),
                sp.GetRequiredService<ILogger<CorridorClient>>()));

        return services;
    }
}

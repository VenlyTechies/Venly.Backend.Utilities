using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Venly.Rails.Helper;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRailsClient"/> as a typed HttpClient.
    ///
    /// <para>Only a process that needs to PULL from the payment provider should call this. WorkflowService does,
    /// so a reconciliation schedule can fetch balances without an operator pushing them; WalletService itself
    /// deliberately does NOT — the ledger never calls a provider, directly or through a hop.</para>
    /// </summary>
    public static IServiceCollection AddRailsClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RailsClientOptions>(configuration.GetSection(RailsClientOptions.SectionName));

        services.AddHttpClient<IRailsClient, RailsClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<RailsClientOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        // The same RailsClient, reached through the maintenance interface as well.
        //
        // Resolved from the typed client the registration above created rather than registered as a second
        // HttpClient: two clients would mean two connection pools and two timeout settings for one target,
        // and they would drift. What the second interface buys is that a caller must ask for the WRITE
        // capability by name — IRailsClient stays read-only, which WalletService's reconciliation relies on.
        services.AddScoped<IRatesMaintenanceClient>(sp =>
            (RailsClient)sp.GetRequiredService<IRailsClient>());

        return services;
    }
}

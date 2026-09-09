using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Venly.Wallet.Helper;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWalletMaintenanceClient"/> as a typed HttpClient.
    ///
    /// <para>Only the process that runs wallet maintenance should call this. A service that merely moves money
    /// has no business holding WalletService's HMAC secret — the same separation
    /// <c>AddAuditMaintenanceClient</c> draws.</para>
    /// </summary>
    public static IServiceCollection AddWalletMaintenanceClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WalletClientOptions>(configuration.GetSection(WalletClientOptions.SectionName));

        services.AddHttpClient<IWalletMaintenanceClient, WalletMaintenanceClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WalletClientOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IWalletIntentClient"/> as a typed HttpClient, reading the SAME
    /// <see cref="WalletClientOptions"/>.
    ///
    /// <para>A separate call from <see cref="AddWalletMaintenanceClient"/> even though both read one section,
    /// because the two capabilities are separate: a process that moves money per movement has no business
    /// holding the ability to trigger a reconciliation sweep, and a scheduler has no business confirming a
    /// payment. PaymentService calls this one; WorkflowService calls the other.</para>
    /// </summary>
    /// <summary>
    /// Registers <see cref="IWalletProvisioningClient"/> as a typed HttpClient, reading the same
    /// <see cref="WalletClientOptions"/> as its two siblings.
    ///
    /// <para>Its own call for the reason the other two are separate: this grants the ability to CREATE a
    /// wallet and nothing else. CustomerService calls it — it decides who is verified — and must not thereby
    /// acquire the ability to move a balance or trigger a sweep.</para>
    /// </summary>
    public static IServiceCollection AddWalletProvisioningClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WalletClientOptions>(configuration.GetSection(WalletClientOptions.SectionName));

        services.AddHttpClient<IWalletProvisioningClient, WalletProvisioningClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WalletClientOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        return services;
    }

    public static IServiceCollection AddWalletIntentClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WalletClientOptions>(configuration.GetSection(WalletClientOptions.SectionName));

        services.AddHttpClient<IWalletIntentClient, WalletIntentClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WalletClientOptions>>().Value;

            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl);

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
        });

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
namespace Venly.Backend.Common.Hmac;
public static class HmacServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisHmacNonceStore"/> as the <see cref="IHmacNonceStore"/>, with the Redis connection
    /// from <c>ConnectionStrings:Redis</c>. Every service that has an <see cref="HmacAuthorizeAttribute"/> action
    /// needs SOME store, because the filter takes one; the no-op store reserves nothing and leaves a captured signed
    /// request replayable for the whole timestamp window, so a service with state-changing internal routes uses this.
    ///
    /// <para>A connection already registered by the service is reused, not replaced: the service may use Redis for
    /// its own reasons. Connecting does not abort on failure, so a Redis blip delays nonce checks rather than stopping
    /// the service booting; while Redis is down a nonce reservation fails closed and the request is refused.</para>
    /// </summary>
    public static IServiceCollection AddRedisHmacNonceStore(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connection);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IHmacNonceStore, RedisHmacNonceStore>();
        return services;
    }
}

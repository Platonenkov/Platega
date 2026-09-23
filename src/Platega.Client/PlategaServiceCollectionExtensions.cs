using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Platega;
using Platega.Balances;
using Platega.Callbacks;
using Platega.Http;
using Platega.Payments;
using Platega.Payouts;
using Platega.Refunds;
using Platega.Subscriptions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of the Platega client.</summary>
public static class PlategaServiceCollectionExtensions
{
    /// <summary>Registers the Platega client bound to a configuration section (usually <c>Platega</c>).</summary>
    public static IServiceCollection AddPlatega(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // OptionsBuilder.Bind registers a change token source, so a rotated key is picked up by IOptionsMonitor.
        services.AddOptions<PlategaOptions>().Bind(configuration);
        return services.AddPlatega(static _ => { });
    }

    /// <summary>Registers the Platega client configured by a delegate.</summary>
    public static IServiceCollection AddPlatega(this IServiceCollection services, Action<PlategaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PlategaOptions>()
            .Configure(configure)
            .Validate(options => options.MerchantId != Guid.Empty, "Platega:MerchantId is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Secret), "Platega:Secret is required.")
            .Validate(options => options.BaseAddress is { IsAbsoluteUri: true }, "Platega:BaseAddress must be an absolute URI.")
            .Validate(options => options.Timeout > TimeSpan.Zero, "Platega:Timeout must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddTransient<PlategaAuthHandler>();

        services.AddHttpClient<PlategaConnection>(ConfigureHttpClient)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .AddHttpMessageHandler<PlategaAuthHandler>();
        services.AddHttpClient<IPlategaPayoutsClient, PlategaPayoutsClient>(ConfigureHttpClient)
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);

        services.TryAddTransient<IPlategaPaymentsClient, PlategaPaymentsClient>();
        services.TryAddTransient<IPlategaRefundsClient, PlategaRefundsClient>();
        services.TryAddTransient<IPlategaBalancesClient, PlategaBalancesClient>();
        services.TryAddTransient<IPlategaSubscriptionsClient, PlategaSubscriptionsClient>();
        services.TryAddTransient<IPlategaClient, PlategaClient>();
        services.TryAddSingleton<PlategaCallbackParser>();

        return services;
    }

    /// <summary>
    /// Redirects are disabled: on a redirect the default handler drops <c>Authorization</c> but forwards custom headers,
    /// so <c>X-Secret</c> would reach whatever host the <c>Location</c> points to.
    /// </summary>
    private static HttpMessageHandler CreatePrimaryHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private static void ConfigureHttpClient(IServiceProvider serviceProvider, HttpClient httpClient)
    {
        PlategaOptions options = serviceProvider.GetRequiredService<IOptions<PlategaOptions>>().Value;
        string baseAddress = options.BaseAddress.AbsoluteUri;
        httpClient.BaseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
        httpClient.Timeout = options.Timeout;
    }
}

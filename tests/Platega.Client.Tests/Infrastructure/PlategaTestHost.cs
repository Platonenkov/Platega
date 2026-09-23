using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;

namespace Platega.Tests.Infrastructure;

/// <summary>Builds a real DI container with every Platega HTTP client routed to <see cref="StubHttpHandler"/>.</summary>
internal sealed class PlategaTestHost : IDisposable
{
    public static readonly Guid MerchantId = Guid.Parse("29ef0000-0000-0000-0000-000000000001");
    public const string Secret = "test-api-secret";
    public const string PayoutSecret = "test-payout-secret";
    public static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1719403200);

    private readonly ServiceProvider _provider;

    public PlategaTestHost(bool withPayoutSecret = true)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        services.AddPlatega(options =>
        {
            options.MerchantId = MerchantId;
            options.Secret = Secret;
            options.BaseAddress = new Uri("https://api.test/");
            options.PayoutSecret = withPayoutSecret ? PayoutSecret : null;
        });
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = Handler));
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public StubHttpHandler Handler { get; } = new StubHttpHandler();

    public IPlategaClient Client => _provider.GetRequiredService<IPlategaClient>();

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    public void Dispose() => _provider.Dispose();
}

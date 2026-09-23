using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Platega.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddPlatega_BindsConfigurationSection()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platega:MerchantId"] = "29ef0000-0000-0000-0000-000000000001",
                ["Platega:Secret"] = "secret",
                ["Platega:BaseAddress"] = "http://localhost:5100",
                ["Platega:Timeout"] = "00:00:10",
            })
            .Build();

        ServiceCollection services = new ServiceCollection();
        services.AddPlatega(configuration.GetSection(PlategaOptions.SectionName));
        using ServiceProvider provider = services.BuildServiceProvider();

        PlategaOptions options = provider.GetRequiredService<IOptions<PlategaOptions>>().Value;
        Assert.Equal(Guid.Parse("29ef0000-0000-0000-0000-000000000001"), options.MerchantId);
        Assert.Equal(new Uri("http://localhost:5100"), options.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(10), options.Timeout);
        Assert.False(options.PayoutsEnabled);
        Assert.NotNull(provider.GetRequiredService<IPlategaClient>().Payments);
    }

    [Fact]
    public void AddPlatega_PicksUpRotatedSecretOnConfigurationReload()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Platega:MerchantId"] = "29ef0000-0000-0000-0000-000000000001",
                ["Platega:Secret"] = "old-secret",
            })
            .Build();

        ServiceCollection services = new ServiceCollection();
        services.AddPlatega(configuration.GetSection(PlategaOptions.SectionName));
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<PlategaOptions> monitor = provider.GetRequiredService<IOptionsMonitor<PlategaOptions>>();
        Assert.Equal("old-secret", monitor.CurrentValue.Secret);

        configuration["Platega:Secret"] = "new-secret";
        configuration.Reload();

        Assert.Equal("new-secret", monitor.CurrentValue.Secret);
    }

    [Fact]
    public void AddPlatega_FailsValidationWithoutCredentials()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddPlatega(_ => { });
        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<PlategaOptions>>().Value);

        Assert.Contains("Platega:MerchantId is required.", exception.Failures);
        Assert.Contains("Platega:Secret is required.", exception.Failures);
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platega.Callbacks;
using Platega.Demo.Data.Services;

namespace Platega.Demo.Data;

public static class DemoDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared SQLite store, the Platega client and the demo services.
    /// <c>Demo:DatabasePath</c> overrides the default <c>%LOCALAPPDATA%/PlategaDemo/demo.db</c>.
    /// </summary>
    public static IServiceCollection AddPlategaDemo(this IServiceCollection services, IConfiguration configuration)
    {
        string databasePath = configuration["Demo:DatabasePath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlategaDemo", "demo.db");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();

        services.AddDbContextFactory<DemoDbContext>(options => options.UseSqlite(connectionString));
        services.AddPlatega(configuration.GetSection(PlategaOptions.SectionName));
        services.AddOptions<ShopOptions>().Bind(configuration.GetSection(ShopOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<OrderService>();
        services.AddTransient<PaymentSyncService>();
        services.AddTransient<RefundService>();
        services.AddTransient<SubscriptionService>();
        services.AddTransient<CallbackProcessor>();
        services.AddTransient<IPlategaCallbackHandler>(provider => provider.GetRequiredService<CallbackProcessor>());
        return services;
    }

    /// <summary>Creates the schema if needed and switches SQLite to WAL, since two processes share the file.</summary>
    public static async Task InitializeDemoDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        IDbContextFactory<DemoDbContext> factory = services.GetRequiredService<IDbContextFactory<DemoDbContext>>();
        await using DemoDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
    }
}

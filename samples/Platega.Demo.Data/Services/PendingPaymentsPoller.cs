using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platega.Subscriptions;

namespace Platega.Demo.Data.Services;

/// <summary>
/// Safety net for lost callbacks (and the only update path when callbacks cannot reach a local machine):
/// periodically re-checks recent pending payments and subscriptions awaiting binding.
/// </summary>
public sealed class PendingPaymentsPoller(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PendingPaymentsPoller> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Pending payments poll failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDbContextFactory<DemoDbContext> dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DemoDbContext>>();
        PaymentSyncService paymentSync = scope.ServiceProvider.GetRequiredService<PaymentSyncService>();
        SubscriptionService subscriptionService = scope.ServiceProvider.GetRequiredService<SubscriptionService>();

        DateTimeOffset since = timeProvider.GetUtcNow() - MaxAge;
        List<Guid> orderIds;
        List<Guid> subscriptionIds;
        await using (DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            orderIds = await db.Orders
                .Where(order => order.Status == PaymentStatus.Pending && order.TransactionId != null && order.CreatedAt >= since)
                .Select(order => order.Id)
                .ToListAsync(cancellationToken);
            subscriptionIds = await db.Subscriptions
                .Where(subscription => subscription.Status == SubscriptionStatus.PendingAgreement && subscription.CreatedAt >= since)
                .Select(subscription => subscription.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (Guid orderId in orderIds)
        {
            await SafeAsync(() => paymentSync.SyncOrderAsync(orderId, cancellationToken), "order", orderId);
        }

        foreach (Guid subscriptionId in subscriptionIds)
        {
            await SafeAsync(() => subscriptionService.SyncAsync(subscriptionId, cancellationToken), "subscription", subscriptionId);
        }
    }

    private async Task SafeAsync(Func<Task> action, string kind, Guid id)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is PlategaApiException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Could not refresh {Kind} {Id}: {Error}", kind, id, exception.Message);
        }
    }
}

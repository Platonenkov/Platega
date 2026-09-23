using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platega.Callbacks;

namespace Platega.Demo.Data.Services;

/// <summary>
/// Handles Platega callbacks. Every callback is written to the audit log first; the state change itself is taken
/// from the API rather than from the callback body, because callbacks are authenticated only by a static secret.
/// Throwing makes the endpoint answer 500, so Platega retries the delivery.
/// </summary>
public sealed class CallbackProcessor(
    IDbContextFactory<DemoDbContext> dbFactory,
    PaymentSyncService paymentSync,
    SubscriptionService subscriptionService,
    TimeProvider timeProvider,
    ILogger<CallbackProcessor> logger) : IPlategaCallbackHandler
{
    public async Task HandleAsync(PlategaCallback callback, CancellationToken cancellationToken)
    {
        await LogAsync(
            new CallbackLogEntry
            {
                ReceivedAt = timeProvider.GetUtcNow(),
                Accepted = true,
                Kind = callback.Kind.ToString(),
                ObjectId = callback.Id,
                Status = callback.RawStatus,
                RawBody = callback.RawJson,
            },
            cancellationToken);

        switch (callback.Kind)
        {
            case PlategaCallbackKind.Payment:
                Order? order = await paymentSync.SyncTransactionAsync(callback.Id, cancellationToken);
                if (order is not null && order.Status != callback.PaymentStatus)
                {
                    logger.LogWarning(
                        "Callback for {TransactionId} says {CallbackStatus}, API says {ApiStatus}; API wins",
                        callback.Id,
                        callback.PaymentStatus,
                        order.Status);
                }

                break;

            case PlategaCallbackKind.SubscriptionCharge when callback.SubscriptionId is { } subscriptionId:
                await subscriptionService.RecordChargeAsync(subscriptionId, callback.Id, callback.PaymentStatus, callback.Amount ?? 0m, cancellationToken);
                await subscriptionService.SyncAsync(subscriptionId, cancellationToken);
                break;

            case PlategaCallbackKind.SubscriptionStatusChanged:
                await subscriptionService.SyncAsync(callback.SubscriptionId ?? callback.Id, cancellationToken);
                break;
        }
    }

    public Task OnRejectedAsync(PlategaCallbackRejection rejection, CancellationToken cancellationToken) =>
        LogAsync(
            new CallbackLogEntry
            {
                ReceivedAt = timeProvider.GetUtcNow(),
                Accepted = false,
                Error = $"{rejection.Reason}: {rejection.Detail}",
                RawBody = rejection.RawBody.Length > 4000 ? rejection.RawBody[..4000] : rejection.RawBody,
            },
            cancellationToken);

    public async Task<IReadOnlyList<CallbackLogEntry>> RecentAsync(int take, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CallbackLog.AsNoTracking().OrderByDescending(entry => entry.ReceivedAt).Take(take).ToListAsync(cancellationToken);
    }

    private async Task LogAsync(CallbackLogEntry entry, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.CallbackLog.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }
}

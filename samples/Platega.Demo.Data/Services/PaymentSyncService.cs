using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platega.Payments;

namespace Platega.Demo.Data.Services;

/// <summary>
/// Pulls the authoritative transaction status from the Platega API into the local order.
/// Used by the callback handler (to verify a callback before trusting it), the poller and the result page,
/// which may run concurrently: updates use optimistic concurrency and never move a status backwards.
/// </summary>
public sealed class PaymentSyncService(
    IDbContextFactory<DemoDbContext> dbFactory,
    IPlategaPaymentsClient payments,
    TimeProvider timeProvider,
    ILogger<PaymentSyncService> logger)
{
    private const int MaxAttempts = 3;

    public Task<Order?> SyncOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        SyncAsync(db => db.Orders.FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken), cancellationToken);

    /// <summary>
    /// Syncs the order that owns <paramref name="transactionId"/>. When no order has this transaction yet
    /// (the create call timed out after Platega had created it), <paramref name="payload"/> (the order id sent
    /// at creation) links the transaction to its order.
    /// </summary>
    public async Task<Order?> SyncTransactionAsync(Guid transactionId, string? payload, CancellationToken cancellationToken)
    {
        Order? order = await SyncAsync(
            db => db.Orders.FirstOrDefaultAsync(item => item.TransactionId == transactionId, cancellationToken),
            cancellationToken);
        if (order is not null)
        {
            return order;
        }

        if (!Guid.TryParse(payload, out Guid orderId) || !await TryAttachAsync(orderId, transactionId, cancellationToken))
        {
            logger.LogWarning("Transaction {TransactionId} does not belong to any order", transactionId);
            return null;
        }

        return await SyncOrderAsync(orderId, cancellationToken);
    }

    /// <summary>Rank of a status in the payment lifecycle; a sync never replaces a status with a lower-ranked one.</summary>
    internal static int Rank(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => 0,
        PaymentStatus.Confirmed or PaymentStatus.Canceled => 1,
        PaymentStatus.Chargebacked => 2,
        _ => -1,
    };

    private async Task<bool> TryAttachAsync(Guid orderId, Guid transactionId, CancellationToken cancellationToken)
    {
        PlategaTransaction transaction = await payments.GetTransactionAsync(transactionId, cancellationToken);
        if (!string.Equals(transaction.Payload, orderId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Order? order = await db.Orders.FirstOrDefaultAsync(item => item.Id == orderId && item.TransactionId == null, cancellationToken);
        if (order is null)
        {
            return false;
        }

        order.TransactionId = transactionId;
        order.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Order {OrderId} linked to transaction {TransactionId} by payload", orderId, transactionId);
        return true;
    }

    private async Task<Order?> SyncAsync(Func<DemoDbContext, Task<Order?>> find, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
            Order? order = await find(db);
            if (order?.TransactionId is not { } transactionId)
            {
                return order;
            }

            PlategaTransaction transaction = await payments.GetTransactionAsync(transactionId, cancellationToken);
            bool statusChanges = transaction.Status != order.Status && Rank(transaction.Status) >= Rank(order.Status);
            bool methodChanges = transaction.PaymentMethod is not null && transaction.PaymentMethod != order.PaidWith;
            if (!statusChanges && !methodChanges)
            {
                return order;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (statusChanges)
            {
                logger.LogInformation("Order {OrderId}: {Old} -> {New}", order.Id, order.Status, transaction.Status);
                order.Status = transaction.Status;
                order.LastError = null;
                if (transaction.Status == PaymentStatus.Confirmed)
                {
                    order.PaidAt ??= now;
                }

                if (transaction.Status == PaymentStatus.Chargebacked)
                {
                    order.RefundedAt ??= now;
                }
            }

            order.PaidWith = transaction.PaymentMethod ?? order.PaidWith;
            order.UpdatedAt = now;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return order;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                logger.LogInformation("Order {OrderId} changed concurrently, re-reading", order.Id);
            }
        }
    }
}

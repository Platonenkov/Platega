using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platega.Payments;

namespace Platega.Demo.Data.Services;

/// <summary>
/// Pulls the authoritative transaction status from the Platega API into the local order.
/// Used by the callback handler (to verify a callback before trusting it), the poller and the result page.
/// </summary>
public sealed class PaymentSyncService(
    IDbContextFactory<DemoDbContext> dbFactory,
    IPlategaPaymentsClient payments,
    TimeProvider timeProvider,
    ILogger<PaymentSyncService> logger)
{
    public async Task<Order?> SyncOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Order? order = await db.Orders.FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order?.TransactionId is not { } transactionId)
        {
            return order;
        }

        await ApplyAsync(db, order, transactionId, cancellationToken);
        return order;
    }

    public async Task<Order?> SyncTransactionAsync(Guid transactionId, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Order? order = await db.Orders.FirstOrDefaultAsync(item => item.TransactionId == transactionId, cancellationToken);
        if (order is null)
        {
            logger.LogWarning("Transaction {TransactionId} does not belong to any order", transactionId);
            return null;
        }

        await ApplyAsync(db, order, transactionId, cancellationToken);
        return order;
    }

    private async Task ApplyAsync(DemoDbContext db, Order order, Guid transactionId, CancellationToken cancellationToken)
    {
        PlategaTransaction transaction = await payments.GetTransactionAsync(transactionId, cancellationToken);
        if (transaction.Status == order.Status && transaction.PaymentMethod == order.PaidWith)
        {
            return;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        logger.LogInformation("Order {OrderId}: {Old} -> {New}", order.Id, order.Status, transaction.Status);

        order.Status = transaction.Status;
        order.PaidWith = transaction.PaymentMethod;
        order.UpdatedAt = now;
        order.LastError = null;
        if (transaction.Status == PaymentStatus.Confirmed)
        {
            order.PaidAt ??= now;
        }

        if (transaction.Status == PaymentStatus.Chargebacked)
        {
            order.RefundedAt ??= now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

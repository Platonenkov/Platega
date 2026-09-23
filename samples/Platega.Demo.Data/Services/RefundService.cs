using Microsoft.EntityFrameworkCore;
using Platega.Refunds;

namespace Platega.Demo.Data.Services;

/// <summary>Merchant-side refunds: check first, then cancel, then re-read the status.</summary>
public sealed class RefundService(
    IDbContextFactory<DemoDbContext> dbFactory,
    IPlategaRefundsClient refunds,
    PaymentSyncService sync)
{
    public async Task<CancelAvailability> CheckAsync(Guid orderId, CancellationToken cancellationToken) =>
        await refunds.GetCancelAvailabilityAsync(await GetTransactionIdAsync(orderId, cancellationToken), cancellationToken);

    public async Task<CancelResult> RefundAsync(Guid orderId, CancellationToken cancellationToken)
    {
        Guid transactionId = await GetTransactionIdAsync(orderId, cancellationToken);
        CancelResult result = await refunds.CancelTransactionAsync(transactionId, cancellationToken);
        await sync.SyncOrderAsync(orderId, cancellationToken);
        return result;
    }

    private async Task<Guid> GetTransactionIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        Guid? transactionId = await db.Orders
            .Where(order => order.Id == orderId)
            .Select(order => order.TransactionId)
            .FirstOrDefaultAsync(cancellationToken);
        return transactionId ?? throw new InvalidOperationException("The order has no Platega transaction.");
    }
}

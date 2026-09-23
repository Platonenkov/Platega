using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platega.Subscriptions;

namespace Platega.Demo.Data.Services;

/// <summary>Creates subscriptions and mirrors their state from Platega.</summary>
public sealed class SubscriptionService(
    IDbContextFactory<DemoDbContext> dbFactory,
    IPlategaSubscriptionsClient subscriptions,
    TimeProvider timeProvider,
    ILogger<SubscriptionService> logger)
{
    public async Task<SubscriptionRecord> CreateAsync(string planCode, CancellationToken cancellationToken)
    {
        SubscriptionPlan plan = DemoCatalog.FindPlan(planCode)
            ?? throw new ArgumentException($"Unknown plan '{planCode}'.", nameof(planCode));

        CreatedSubscription created = await subscriptions.CreateAsync(
            new CreateSubscriptionRequest
            {
                Amount = plan.Amount,
                Interval = plan.Interval,
                IntervalCount = plan.IntervalCount,
                Description = $"Подписка «{plan.Name}»",
            },
            cancellationToken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        SubscriptionRecord record = new SubscriptionRecord
        {
            Id = created.SubscriptionId,
            PlanCode = plan.Code,
            PlanName = plan.Name,
            Amount = plan.Amount,
            Interval = plan.Interval,
            IntervalCount = plan.IntervalCount,
            RedirectUrl = created.RedirectUrl,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Subscriptions.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<SubscriptionRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Subscriptions
            .AsNoTracking()
            .Include(item => item.Charges)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Refreshes a subscription from <c>GET /subscription/{id}</c>. Returns null for subscriptions unknown locally.</summary>
    public async Task<SubscriptionRecord?> SyncAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        SubscriptionRecord? record = await db.Subscriptions.FirstOrDefaultAsync(item => item.Id == subscriptionId, cancellationToken);
        if (record is null)
        {
            logger.LogWarning("Subscription {SubscriptionId} is not known locally", subscriptionId);
            return null;
        }

        PlategaSubscription remote = await subscriptions.GetAsync(subscriptionId, cancellationToken);
        record.Status = remote.Status;
        record.CustomerEmail = remote.CustomerEmail;
        record.NextChargeAt = remote.NextChargeAt;
        record.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task CancelAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        await subscriptions.CancelAsync(subscriptionId, cancellationToken);
        await SyncAsync(subscriptionId, cancellationToken);
    }

    /// <summary>Stores a charge once; repeated callbacks for the same charge are ignored.</summary>
    public async Task RecordChargeAsync(Guid subscriptionId, Guid chargeId, PaymentStatus status, decimal amount, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        bool known = await db.Subscriptions.AnyAsync(item => item.Id == subscriptionId, cancellationToken);
        bool duplicate = await db.SubscriptionCharges.AnyAsync(item => item.Id == chargeId, cancellationToken);
        if (!known || duplicate)
        {
            return;
        }

        db.SubscriptionCharges.Add(new SubscriptionChargeRecord
        {
            Id = chargeId,
            SubscriptionId = subscriptionId,
            Status = status,
            Amount = amount,
            ReceivedAt = timeProvider.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

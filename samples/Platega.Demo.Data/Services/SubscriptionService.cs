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
    private const int MaxAttempts = 3;

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

    /// <summary>
    /// Refreshes a subscription from <c>GET /subscription/{id}</c>. A subscription missing locally
    /// (for example, the local save failed after Platega created it) is recreated from the API.
    /// </summary>
    public async Task<SubscriptionRecord> SyncAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            PlategaSubscription remote = await subscriptions.GetAsync(subscriptionId, cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();

            await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
            SubscriptionRecord? record = await db.Subscriptions.FirstOrDefaultAsync(item => item.Id == subscriptionId, cancellationToken);
            if (record is null)
            {
                logger.LogWarning("Subscription {SubscriptionId} was unknown locally, restoring it from the API", subscriptionId);
                record = new SubscriptionRecord
                {
                    Id = subscriptionId,
                    PlanCode = "external",
                    PlanName = remote.Description ?? "Без тарифа",
                    Amount = (int)remote.Amount,
                    Interval = remote.IntervalUnit,
                    IntervalCount = remote.IntervalCount,
                    CreatedAt = remote.CreatedAt ?? now,
                };
                db.Subscriptions.Add(record);
            }

            if (Rank(remote.Status) >= Rank(record.Status))
            {
                record.Status = remote.Status;
            }

            record.CustomerEmail = remote.CustomerEmail ?? record.CustomerEmail;
            record.NextChargeAt = remote.NextChargeAt;
            record.UpdatedAt = now;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return record;
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                logger.LogInformation("Subscription {SubscriptionId} changed concurrently, re-reading", subscriptionId);
            }
        }
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
        if (!await db.Subscriptions.AnyAsync(item => item.Id == subscriptionId, cancellationToken))
        {
            throw new InvalidOperationException($"Subscription {subscriptionId} is unknown; the charge callback will be retried.");
        }

        if (await db.SubscriptionCharges.AnyAsync(item => item.Id == chargeId, cancellationToken))
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent delivery of the same callback may have stored it first; anything else is a real failure.
            if (!await IsRecordedAsync(chargeId, cancellationToken))
            {
                throw;
            }
        }
    }

    /// <summary>Cancelled and Failed are final; a stale read must not bring them back.</summary>
    internal static int Rank(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingAgreement => 0,
        SubscriptionStatus.Active or SubscriptionStatus.PastDue => 1,
        SubscriptionStatus.Cancelled or SubscriptionStatus.Failed => 2,
        _ => -1,
    };

    private async Task<bool> IsRecordedAsync(Guid chargeId, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SubscriptionCharges.AnyAsync(item => item.Id == chargeId, cancellationToken);
    }
}

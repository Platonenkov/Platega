namespace Platega.Subscriptions;

/// <summary>
/// Parameters of a recurring SBP subscription. No money moves at creation time:
/// the payer confirms the binding on the Platega page, then Platega charges <see cref="Amount"/> every period.
/// </summary>
public sealed record CreateSubscriptionRequest
{
    /// <summary>Amount of one regular charge, whole units of <see cref="Currency"/>.</summary>
    public required int Amount { get; init; }

    /// <summary>Charge currency; Platega documents <c>RUB</c>.</summary>
    public string Currency { get; init; } = "RUB";

    /// <summary>Period unit between charges.</summary>
    public required SubscriptionInterval Interval { get; init; }

    /// <summary>Number of <see cref="Interval"/> units between charges: day ≤ 31, week ≤ 4, month ≤ 12, year ≤ 3.</summary>
    public int IntervalCount { get; init; } = 1;

    /// <summary>Shown to the payer on the payment page and in email notifications.</summary>
    public required string Description { get; init; }
}

/// <summary>Result of subscription creation.</summary>
public sealed record CreatedSubscription
{
    /// <summary>Subscription id; callbacks and all subscription endpoints use it.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>Binding page. Send the payer there immediately: the binding expires after 30 minutes.</summary>
    public required string RedirectUrl { get; init; }

    /// <summary>Status right after creation as returned by Platega.</summary>
    public string? Status { get; init; }

    /// <summary>Merchant id echoed by the API.</summary>
    public string? MerchantId { get; init; }
}

/// <summary>Subscription returned by <c>GET /subscription/{id}</c> and <c>GET /subscription</c>.</summary>
public sealed record PlategaSubscription
{
    /// <summary>Subscription id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Current subscription status.</summary>
    public SubscriptionStatus Status { get; init; }

    /// <summary>Amount of one charge.</summary>
    public decimal Amount { get; init; }

    /// <summary>Charge currency.</summary>
    public string? CurrencyCode { get; init; }

    /// <summary>Period unit between charges.</summary>
    public SubscriptionInterval IntervalUnit { get; init; }

    /// <summary>Number of period units between charges.</summary>
    public int IntervalCount { get; init; }

    /// <summary>When the subscription became active.</summary>
    public DateTimeOffset? StartAt { get; init; }

    /// <summary>Next scheduled charge.</summary>
    public DateTimeOffset? NextChargeAt { get; init; }

    /// <summary>Last charge.</summary>
    public DateTimeOffset? LastChargeAt { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Description shown to the payer.</summary>
    public string? Description { get; init; }

    /// <summary>Email the payer entered on the binding page.</summary>
    public string? CustomerEmail { get; init; }

    /// <summary>Number of charges; filled in list responses only.</summary>
    public int? ChargesCount { get; init; }

    /// <summary>Charge statistics; filled in single-subscription responses only.</summary>
    public SubscriptionChargeMetrics? ChargeMetrics { get; init; }
}

/// <summary>Charge statistics of a subscription.</summary>
public sealed record SubscriptionChargeMetrics
{
    /// <summary>Number of charge attempts.</summary>
    public int ChargesTotal { get; init; }

    /// <summary>Number of successful charges.</summary>
    public int ChargesSuccess { get; init; }

    /// <summary>Number of failed charges.</summary>
    public int ChargesFailed { get; init; }

    /// <summary>Total amount charged.</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Last charge.</summary>
    public DateTimeOffset? LastChargeAt { get; init; }

    /// <summary>Next scheduled charge.</summary>
    public DateTimeOffset? NextChargeAt { get; init; }
}

/// <summary>Filters of <c>GET /subscription</c>.</summary>
public sealed record SubscriptionListFilter
{
    /// <summary>Only subscriptions in this status; <c>null</c> for all.</summary>
    public SubscriptionStatus? Status { get; init; }

    /// <summary>Created at or after this time.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Created at or before this time.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size.</summary>
    public int Size { get; init; } = 20;
}

/// <summary>One page of subscriptions.</summary>
public sealed record SubscriptionPage
{
    /// <summary>Subscriptions on this page.</summary>
    public IReadOnlyList<PlategaSubscription> Items { get; init; } = [];

    /// <summary>Total number of matching subscriptions.</summary>
    public int Total { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; }

    /// <summary>Page size.</summary>
    public int Size { get; init; }
}

/// <summary>Result of <c>POST /subscription/{id}/cancel</c>. The call is idempotent.</summary>
public sealed record SubscriptionCancelResult
{
    /// <summary>Cancelled subscription id.</summary>
    public Guid SubscriptionId { get; init; }

    /// <summary>Status reported by Platega, e.g. <c>cancelled</c>.</summary>
    public string? Status { get; init; }
}

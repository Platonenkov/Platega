namespace Platega.Subscriptions;

/// <summary>
/// Parameters of a recurring SBP subscription. No money moves at creation time:
/// the payer confirms the binding on the Platega page, then Platega charges <see cref="Amount"/> every period.
/// </summary>
public sealed record CreateSubscriptionRequest
{
    /// <summary>Amount of one regular charge, whole units of <see cref="Currency"/>.</summary>
    public required int Amount { get; init; }

    public string Currency { get; init; } = "RUB";

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

    public string? Status { get; init; }

    public string? MerchantId { get; init; }
}

/// <summary>Subscription returned by <c>GET /subscription/{id}</c> and <c>GET /subscription</c>.</summary>
public sealed record PlategaSubscription
{
    public required Guid Id { get; init; }

    public SubscriptionStatus Status { get; init; }

    public decimal Amount { get; init; }

    public string? CurrencyCode { get; init; }

    public SubscriptionInterval IntervalUnit { get; init; }

    public int IntervalCount { get; init; }

    public DateTimeOffset? StartAt { get; init; }

    public DateTimeOffset? NextChargeAt { get; init; }

    public DateTimeOffset? LastChargeAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? Description { get; init; }

    public string? CustomerEmail { get; init; }

    /// <summary>Number of charges; filled in list responses only.</summary>
    public int? ChargesCount { get; init; }

    /// <summary>Charge statistics; filled in single-subscription responses only.</summary>
    public SubscriptionChargeMetrics? ChargeMetrics { get; init; }
}

/// <summary>Charge statistics of a subscription.</summary>
public sealed record SubscriptionChargeMetrics
{
    public int ChargesTotal { get; init; }

    public int ChargesSuccess { get; init; }

    public int ChargesFailed { get; init; }

    public decimal TotalAmount { get; init; }

    public DateTimeOffset? LastChargeAt { get; init; }

    public DateTimeOffset? NextChargeAt { get; init; }
}

/// <summary>Filters of <c>GET /subscription</c>.</summary>
public sealed record SubscriptionListFilter
{
    public SubscriptionStatus? Status { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    public int Size { get; init; } = 20;
}

/// <summary>One page of subscriptions.</summary>
public sealed record SubscriptionPage
{
    public IReadOnlyList<PlategaSubscription> Items { get; init; } = [];

    public int Total { get; init; }

    public int Page { get; init; }

    public int Size { get; init; }
}

/// <summary>Result of <c>POST /subscription/{id}/cancel</c>. The call is idempotent.</summary>
public sealed record SubscriptionCancelResult
{
    public Guid SubscriptionId { get; init; }

    public string? Status { get; init; }
}

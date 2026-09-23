namespace Platega.Subscriptions;

internal sealed record CreateSubscriptionWireRequest
{
    public required int PaymentMethod { get; init; }

    public required SubscriptionWireDetails PaymentDetails { get; init; }

    public required string Description { get; init; }
}

internal sealed record SubscriptionWireDetails
{
    public required int Amount { get; init; }

    public required string Currency { get; init; }

    public required SubscriptionInterval Interval { get; init; }

    public required int IntervalCount { get; init; }
}

internal sealed record CreateSubscriptionWireResponse
{
    public string? PaymentMethod { get; init; }

    public Guid TransactionId { get; init; }

    public string? Redirect { get; init; }

    public string? Status { get; init; }

    public string? MerchantId { get; init; }
}

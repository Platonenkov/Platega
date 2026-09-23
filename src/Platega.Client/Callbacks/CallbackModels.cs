using Platega.Subscriptions;

namespace Platega.Callbacks;

/// <summary>What a callback is about.</summary>
public enum PlategaCallbackKind
{
    /// <summary>Status change of a regular payment.</summary>
    Payment,

    /// <summary>A recurring subscription charge (successful or failed). <see cref="PlategaCallback.Id"/> is the charge transaction id.</summary>
    SubscriptionCharge,

    /// <summary>Subscription status change. <see cref="PlategaCallback.Id"/> equals the subscription id.</summary>
    SubscriptionStatusChanged,
}

/// <summary>
/// Normalized callback. Platega sends payment callbacks in camelCase and subscription callbacks in PascalCase;
/// both are mapped here. Callbacks are retried (up to 3 times, 5 minutes apart), so handlers must be idempotent.
/// </summary>
public sealed record PlategaCallback
{
    public required PlategaCallbackKind Kind { get; init; }

    /// <summary>Transaction id, or the subscription id for <see cref="PlategaCallbackKind.SubscriptionStatusChanged"/>.</summary>
    public required Guid Id { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    /// <summary>Status exactly as sent by Platega.</summary>
    public required string RawStatus { get; init; }

    /// <summary>Payment status; <see cref="PaymentStatus.Unknown"/> for subscription status callbacks.</summary>
    public PaymentStatus PaymentStatus { get; init; }

    /// <summary>Subscription status; <see cref="SubscriptionCallbackStatus.Unknown"/> for payment callbacks.</summary>
    public SubscriptionCallbackStatus SubscriptionStatus { get; init; }

    public PaymentMethod PaymentMethod { get; init; }

    public string? Payload { get; init; }

    public Guid? SubscriptionId { get; init; }

    public DateTimeOffset? NextChargeAt { get; init; }

    /// <summary>Original request body, for auditing.</summary>
    public required string RawJson { get; init; }
}

/// <summary>Why an incoming callback was rejected.</summary>
public enum PlategaCallbackRejectionReason
{
    /// <summary><c>X-MerchantId</c> / <c>X-Secret</c> headers are missing or do not match the configuration.</summary>
    Unauthorized,

    /// <summary>The body is not a valid Platega callback.</summary>
    InvalidPayload,
}

/// <summary>Details of a rejected callback, for logging.</summary>
public sealed record PlategaCallbackRejection(PlategaCallbackRejectionReason Reason, string RawBody, string? Detail);

/// <summary>
/// Application logic for incoming callbacks. Implementations must be idempotent: Platega retries delivery,
/// and callbacks are authenticated only by a static secret, so re-check the transaction via the API before fulfilling an order.
/// </summary>
public interface IPlategaCallbackHandler
{
    Task HandleAsync(PlategaCallback callback, CancellationToken cancellationToken);

    Task OnRejectedAsync(PlategaCallbackRejection rejection, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record CallbackWire
{
    public string? Id { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    public string? Status { get; init; }

    public int? PaymentMethod { get; init; }

    public string? Payload { get; init; }

    public string? SubscriptionId { get; init; }

    public DateTimeOffset? NextChargeAt { get; init; }
}

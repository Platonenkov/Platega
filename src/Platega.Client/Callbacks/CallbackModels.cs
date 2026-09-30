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
    /// <summary>What the callback is about.</summary>
    public required PlategaCallbackKind Kind { get; init; }

    /// <summary>Transaction id, or the subscription id for <see cref="PlategaCallbackKind.SubscriptionStatusChanged"/>.</summary>
    public required Guid Id { get; init; }

    /// <summary>Amount paid by the customer. For SBP it includes the fee charged on top of the order amount, so do not compare it with the order amount for equality.</summary>
    public decimal? Amount { get; init; }

    /// <summary>Currency code of <see cref="Amount"/>.</summary>
    public string? Currency { get; init; }

    /// <summary>Status exactly as sent by Platega.</summary>
    public required string RawStatus { get; init; }

    /// <summary>Payment status; <see cref="PaymentStatus.Unknown"/> for subscription status callbacks.</summary>
    public PaymentStatus PaymentStatus { get; init; }

    /// <summary>Subscription status; <see cref="SubscriptionCallbackStatus.Unknown"/> for payment callbacks.</summary>
    public SubscriptionCallbackStatus SubscriptionStatus { get; init; }

    /// <summary>Payment method; <see cref="PaymentMethod.Unknown"/> when Platega sends <c>null</c> (no method was chosen).</summary>
    public PaymentMethod PaymentMethod { get; init; }

    /// <summary>The <c>payload</c> passed when the payment was created, e.g. your order id.</summary>
    public string? Payload { get; init; }

    /// <summary>Subscription id for subscription callbacks; <c>null</c> for regular payments.</summary>
    public Guid? SubscriptionId { get; init; }

    /// <summary>Next scheduled subscription charge, when Platega reports one.</summary>
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
    /// <summary>Processes an authenticated callback. Throw to answer 500 so that Platega retries the delivery.</summary>
    Task HandleAsync(PlategaCallback callback, CancellationToken cancellationToken);

    /// <summary>Called for requests rejected because of wrong credentials or an invalid body, e.g. to log them.</summary>
    Task OnRejectedAsync(PlategaCallbackRejection rejection, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Called for a reachability probe: Platega checks the callback URL with <c>POST {}</c> when it is saved in the cabinet.
    /// The endpoint answers 200 without invoking <see cref="HandleAsync"/>.
    /// </summary>
    Task OnProbeAsync(PlategaCallbackProbe probe, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>A callback URL reachability probe (empty body or <c>{}</c>).</summary>
/// <param name="Authenticated">True when the probe carried valid <c>X-MerchantId</c>/<c>X-Secret</c> headers.</param>
/// <param name="RawBody">Original request body.</param>
public sealed record PlategaCallbackProbe(bool Authenticated, string RawBody);

internal sealed record CallbackWire
{
    public string? Id { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    public string? Status { get; init; }

    public int? PaymentMethod { get; init; }

    public string? Payload { get; init; }

    public string? SubscriptionId { get; init; }

    /// <summary>Kept as text: a malformed optional date must not cause the whole callback to be rejected.</summary>
    public string? NextChargeAt { get; init; }
}

using Platega.Subscriptions;

namespace Platega.Demo.Data;

/// <summary>Shop order paid through Platega.</summary>
public sealed class Order
{
    public Guid Id { get; set; }

    public required string ProductCode { get; set; }

    public required string ProductName { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    /// <summary>Fixed payment method, or null when the payer chose it on the Platega page.</summary>
    public PaymentMethod? Method { get; set; }

    public required string CustomerName { get; set; }

    public Guid? TransactionId { get; set; }

    public string? PaymentUrl { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Method name reported by Platega after payment, e.g. SBPQR.</summary>
    public string? PaidWith { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset? RefundedAt { get; set; }
}

/// <summary>Recurring SBP subscription mirrored from Platega.</summary>
public sealed class SubscriptionRecord
{
    /// <summary>Platega subscription id.</summary>
    public Guid Id { get; set; }

    public required string PlanCode { get; set; }

    public required string PlanName { get; set; }

    public int Amount { get; set; }

    public SubscriptionInterval Interval { get; set; }

    public int IntervalCount { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingAgreement;

    public string? RedirectUrl { get; set; }

    public string? CustomerEmail { get; set; }

    public DateTimeOffset? NextChargeAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<SubscriptionChargeRecord> Charges { get; set; } = [];
}

/// <summary>One subscription charge; keyed by the charge transaction id so repeated callbacks are ignored.</summary>
public sealed class SubscriptionChargeRecord
{
    public Guid Id { get; set; }

    public Guid SubscriptionId { get; set; }

    public PaymentStatus Status { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Audit trail of every incoming callback, accepted or rejected.</summary>
public sealed class CallbackLogEntry
{
    public long Id { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public bool Accepted { get; set; }

    public string? Kind { get; set; }

    public Guid? ObjectId { get; set; }

    public string? Status { get; set; }

    public string? Error { get; set; }

    public required string RawBody { get; set; }
}

using Platega.Subscriptions;

namespace Platega.Serialization;

/// <remarks>
/// Numeric codes are used by transaction exports; confirmed by Platega support on 2026-09-29.
/// </remarks>
internal sealed class PaymentStatusConverter : TolerantEnumConverter<PaymentStatus>
{
    public static readonly PaymentStatusConverter Instance = new PaymentStatusConverter();

    public PaymentStatusConverter()
        : base(
            [
                ("PENDING", PaymentStatus.Pending),
                ("CONFIRMED", PaymentStatus.Confirmed),
                ("CANCELED", PaymentStatus.Canceled),
                ("CANCELLED", PaymentStatus.Canceled),
                ("CHARGEBACKED", PaymentStatus.Chargebacked),
            ],
            [
                (1, PaymentStatus.Pending),
                (6, PaymentStatus.Canceled),
                (7, PaymentStatus.Confirmed),
                (9, PaymentStatus.Chargebacked),
            ],
            PaymentStatus.Unknown,
            writeAsCode: false)
    {
    }
}

/// <remarks>
/// <c>GET /subscription/{id}</c> returns names, <c>GET /subscription</c> returns numeric codes.
/// The numeric mapping follows the declaration order of the documented <c>SubscriptionStatus</c> schema;
/// it is an assumption until verified against the live API.
/// </remarks>
internal sealed class SubscriptionStatusConverter : TolerantEnumConverter<SubscriptionStatus>
{
    public static readonly SubscriptionStatusConverter Instance = new SubscriptionStatusConverter();

    public SubscriptionStatusConverter()
        : base(
            [
                ("PendingAgreement", SubscriptionStatus.PendingAgreement),
                ("Active", SubscriptionStatus.Active),
                ("PastDue", SubscriptionStatus.PastDue),
                ("Cancelled", SubscriptionStatus.Cancelled),
                ("Canceled", SubscriptionStatus.Cancelled),
                ("Failed", SubscriptionStatus.Failed),
            ],
            [
                (0, SubscriptionStatus.PendingAgreement),
                (1, SubscriptionStatus.Active),
                (2, SubscriptionStatus.PastDue),
                (3, SubscriptionStatus.Cancelled),
                (4, SubscriptionStatus.Failed),
            ],
            SubscriptionStatus.Unknown,
            writeAsCode: false)
    {
    }
}

internal sealed class SubscriptionIntervalConverter : TolerantEnumConverter<SubscriptionInterval>
{
    public static readonly SubscriptionIntervalConverter Instance = new SubscriptionIntervalConverter();

    public SubscriptionIntervalConverter()
        : base(
            [
                ("Day", SubscriptionInterval.Day),
                ("Week", SubscriptionInterval.Week),
                ("Month", SubscriptionInterval.Month),
                ("Year", SubscriptionInterval.Year),
            ],
            [
                (1, SubscriptionInterval.Day),
                (2, SubscriptionInterval.Week),
                (3, SubscriptionInterval.Month),
                (4, SubscriptionInterval.Year),
            ],
            SubscriptionInterval.Unknown,
            writeAsCode: true)
    {
    }
}

internal sealed class SubscriptionCallbackStatusConverter : TolerantEnumConverter<SubscriptionCallbackStatus>
{
    public static readonly SubscriptionCallbackStatusConverter Instance = new SubscriptionCallbackStatusConverter();

    public SubscriptionCallbackStatusConverter()
        : base(
            [
                ("SUBSCRIPTION_ACTIVATED", SubscriptionCallbackStatus.Activated),
                ("SUBSCRIPTION_PAST_DUE", SubscriptionCallbackStatus.PastDue),
                ("SUBSCRIPTION_CANCELLED", SubscriptionCallbackStatus.Cancelled),
                ("SUBSCRIPTION_FAILED", SubscriptionCallbackStatus.Failed),
            ],
            [],
            SubscriptionCallbackStatus.Unknown,
            writeAsCode: false)
    {
    }
}

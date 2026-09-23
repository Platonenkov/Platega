using System.Text.Json.Serialization;
using Platega.Serialization;

namespace Platega.Subscriptions;

/// <summary>Subscription state.</summary>
[JsonConverter(typeof(SubscriptionStatusConverter))]
public enum SubscriptionStatus
{
    Unknown = 0,

    /// <summary>Created; the payer has not yet confirmed the SBP account binding (30 minutes are given).</summary>
    PendingAgreement,

    /// <summary>Charges run on schedule.</summary>
    Active,

    /// <summary>A charge failed; no further automatic attempts are made.</summary>
    PastDue,

    /// <summary>Cancelled by the merchant or the payer.</summary>
    Cancelled,

    /// <summary>The binding could not be confirmed during activation.</summary>
    Failed,
}

/// <summary>Charge period unit.</summary>
[JsonConverter(typeof(SubscriptionIntervalConverter))]
public enum SubscriptionInterval
{
    Unknown = 0,
    Day = 1,
    Week = 2,
    Month = 3,
    Year = 4,
}

/// <summary>Status carried by the subscription status callback (<c>SUBSCRIPTION_*</c>).</summary>
[JsonConverter(typeof(SubscriptionCallbackStatusConverter))]
public enum SubscriptionCallbackStatus
{
    Unknown = 0,
    Activated,
    PastDue,
    Cancelled,
    Failed,
}

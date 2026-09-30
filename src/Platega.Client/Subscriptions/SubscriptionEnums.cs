using System.Text.Json.Serialization;
using Platega.Serialization;

namespace Platega.Subscriptions;

/// <summary>Subscription state.</summary>
[JsonConverter(typeof(SubscriptionStatusConverter))]
public enum SubscriptionStatus
{
    /// <summary>Status not recognized by this version of the client.</summary>
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
    /// <summary>Interval not recognized.</summary>
    Unknown = 0,
    /// <summary>Days.</summary>
    Day = 1,
    /// <summary>Weeks.</summary>
    Week = 2,
    /// <summary>Months.</summary>
    Month = 3,
    /// <summary>Years.</summary>
    Year = 4,
}

/// <summary>Status carried by the subscription status callback (<c>SUBSCRIPTION_*</c>).</summary>
[JsonConverter(typeof(SubscriptionCallbackStatusConverter))]
public enum SubscriptionCallbackStatus
{
    /// <summary>Status not recognized.</summary>
    Unknown = 0,
    /// <summary>The binding was confirmed; charges run on schedule.</summary>
    Activated,
    /// <summary>A charge failed; no further automatic attempts are made.</summary>
    PastDue,
    /// <summary>Cancelled by the merchant or the payer.</summary>
    Cancelled,
    /// <summary>The binding could not be confirmed.</summary>
    Failed,
}

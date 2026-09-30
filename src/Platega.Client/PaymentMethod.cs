namespace Platega;

/// <summary>
/// Payment method identifiers accepted by <c>POST /transaction/process</c> and reported in callbacks.
/// Serialized as numbers.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Method not recognized or not chosen yet.</summary>
    Unknown = 0,

    /// <summary>SBP (Russian Faster Payments System), QR code.</summary>
    SbpQr = 2,

    /// <summary>ERIP (Belarus).</summary>
    Erip = 3,

    /// <summary>Recurring SBP subscription. Used only when creating subscriptions.</summary>
    Subscription = 6,

    /// <summary>Card acquiring.</summary>
    Card = 11,

    /// <summary>International payment.</summary>
    International = 12,

    /// <summary>Cryptocurrency.</summary>
    Crypto = 13,

    /// <summary>SberPay.</summary>
    SberPay = 14,
}

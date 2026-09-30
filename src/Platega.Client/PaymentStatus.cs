using System.Text.Json.Serialization;
using Platega.Serialization;

namespace Platega;

/// <summary>
/// Transaction status. Unrecognized values are read as <see cref="Unknown"/> instead of failing.
/// </summary>
[JsonConverter(typeof(PaymentStatusConverter))]
public enum PaymentStatus
{
    /// <summary>Status not recognized by this version of the client.</summary>
    Unknown = 0,
    /// <summary>Created and waiting for the payment (export code 1).</summary>
    Pending,
    /// <summary>Paid successfully (export code 7).</summary>
    Confirmed,
    /// <summary>Not paid: declined or expired (export code 6). Platega cancels expired payments in batches, which in practice happens hours after the link expires.</summary>
    Canceled,
    /// <summary>Refunded (export code 9).</summary>
    Chargebacked,
}

using System.Text.Json.Serialization;
using Platega.Serialization;

namespace Platega;

/// <summary>
/// Transaction status. Unrecognized values are read as <see cref="Unknown"/> instead of failing.
/// </summary>
[JsonConverter(typeof(PaymentStatusConverter))]
public enum PaymentStatus
{
    Unknown = 0,
    Pending,
    Confirmed,
    Canceled,
    Chargebacked,
}

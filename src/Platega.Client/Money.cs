using System.Globalization;

namespace Platega;

/// <summary>
/// Amount with its currency code (<c>paymentDetails</c> in the API). The currency is kept as a string on purpose:
/// merchants may receive RUB, BYN (ERIP), USDT or other currencies, and an unknown code must never be read as RUB.
/// </summary>
public sealed record Money(decimal Amount, string Currency)
{
    public override string ToString() =>
        $"{Amount.ToString("0.##", CultureInfo.InvariantCulture)} {Currency}".TrimEnd();
}

namespace Platega.FakeServer;

/// <summary>Settings of the Platega emulator. Credentials here are fake and must match the demo apps' configuration.</summary>
public sealed class FakeOptions
{
    public const string SectionName = "Fake";

    public Guid MerchantId { get; set; }

    public string Secret { get; set; } = string.Empty;

    public string PayoutSecret { get; set; } = string.Empty;

    /// <summary>Public address of the emulator, used to build payment page links.</summary>
    public Uri PublicBaseUrl { get; set; } = new Uri("http://localhost:5190/");

    /// <summary>Where callbacks are delivered (the Admin demo webhook). Empty disables callbacks.</summary>
    public Uri? CallbackUrl { get; set; }

    /// <summary>Lifetime of a payment link; Platega uses 15 minutes.</summary>
    public TimeSpan PaymentLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>RUB per USDT used for conversions.</summary>
    public decimal UsdtRate { get; set; } = 91.2m;

    /// <summary>Commission withheld from each confirmed payment, as a fraction.</summary>
    public decimal CommissionRate { get; set; } = 0.05m;

    /// <summary>Initial USDT balance available for payouts and refunds.</summary>
    public decimal InitialUsdtBalance { get; set; } = 1000m;
}

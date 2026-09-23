namespace Platega;

/// <summary>
/// Connection settings for the Platega API.
/// </summary>
public sealed class PlategaOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "Platega";

    /// <summary>Production API address.</summary>
    public static readonly Uri DefaultBaseAddress = new Uri("https://app.platega.io/");

    /// <summary>Merchant identifier (<c>X-MerchantId</c>).</summary>
    public Guid MerchantId { get; set; }

    /// <summary>API key (<c>X-Secret</c>). Also used to authenticate incoming callbacks.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>API base address. Override it to target a test double such as Platega.FakeServer.</summary>
    public Uri BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>
    /// Secret for the Payout API (HMAC-SHA256 request signing). Optional: payouts are enabled per merchant on request.
    /// </summary>
    public string? PayoutSecret { get; set; }

    /// <summary>HTTP timeout for a single API call.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>True when a Payout API secret is configured.</summary>
    public bool PayoutsEnabled => !string.IsNullOrWhiteSpace(PayoutSecret);
}

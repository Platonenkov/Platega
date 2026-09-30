namespace Platega.Balances;

/// <summary>Merchant balance in one currency (<c>GET /balance/all</c>).</summary>
public sealed record PlategaBalance
{
    /// <summary>Available amount. Can be negative: refunds are charged to the USDT balance.</summary>
    public decimal Amount { get; init; }

    /// <summary>Currency code, e.g. <c>RUB</c> or <c>USDT</c>.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Amount on hold.</summary>
    public decimal FrozenBalance { get; init; }
}

namespace Platega.Balances;

/// <summary>Merchant balance in one currency (<c>GET /balance/all</c>).</summary>
public sealed record PlategaBalance
{
    public decimal Amount { get; init; }

    public string Currency { get; init; } = string.Empty;

    /// <summary>Amount on hold.</summary>
    public decimal FrozenBalance { get; init; }
}

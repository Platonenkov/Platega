namespace Platega.Payouts;

/// <summary>
/// Payout to a RUB card. Set exactly one of <see cref="CardId"/> (saved card) or <see cref="CardNumber"/> (full PAN).
/// </summary>
public sealed record CardPayoutRequest
{
    /// <summary>Minimum amount of one payout, RUB.</summary>
    public const int MinAmountRub = 1000;

    /// <summary>Maximum amount of one payout, RUB.</summary>
    public const int MaxAmountRub = 87500;

    public string? CardId { get; init; }

    public string? CardNumber { get; init; }

    public required int AmountRub { get; init; }
}

/// <summary>Result of <c>POST /api/v1/payouts/card-rub</c>.</summary>
public sealed record CardPayoutResult
{
    public Guid WithdrawalRecordId { get; init; }

    /// <summary><c>CREATED</c> right after creation.</summary>
    public string? Status { get; init; }

    public string? CardMasked { get; init; }

    /// <summary>Amount deducted from the merchant USDT balance.</summary>
    public decimal AmountUsdtDebited { get; init; }

    /// <summary>Idempotency key the request was sent with. Reuse it to retry the same payout safely.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;
}

/// <summary>Saved payout card (<c>GET /api/v1/cards</c>).</summary>
public sealed record SavedCard
{
    public required string CardId { get; init; }

    public string? Masked { get; init; }

    public string? Last4 { get; init; }

    public string? Brand { get; init; }

    public string? Label { get; init; }

    /// <summary><c>ACTIVE</c>, <c>DISABLED</c> or <c>PENDING</c>.</summary>
    public string? Status { get; init; }
}

internal sealed record CardPayoutWireRequest
{
    public string? CardId { get; init; }

    public string? CardNumber { get; init; }

    public required int AmountRub { get; init; }

    public required string PayoutMethod { get; init; }

    public required string CurrencyRequested { get; init; }
}

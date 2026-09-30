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

    /// <summary>Id of a saved card (see <see cref="IPlategaPayoutsClient.GetSavedCardsAsync"/>).</summary>
    public string? CardId { get; init; }

    /// <summary>Full card number (13 to 19 digits, no separators).</summary>
    public string? CardNumber { get; init; }

    /// <summary>Payout amount in RUB, between <see cref="MinAmountRub"/> and <see cref="MaxAmountRub"/>.</summary>
    public required int AmountRub { get; init; }
}

/// <summary>Result of <c>POST /api/v1/payouts/card-rub</c>.</summary>
public sealed record CardPayoutResult
{
    /// <summary>Id of the created payout.</summary>
    public Guid WithdrawalRecordId { get; init; }

    /// <summary><c>CREATED</c> right after creation.</summary>
    public string? Status { get; init; }

    /// <summary>Masked card number.</summary>
    public string? CardMasked { get; init; }

    /// <summary>Amount deducted from the merchant USDT balance.</summary>
    public decimal AmountUsdtDebited { get; init; }

    /// <summary>Idempotency key the request was sent with. Reuse it to retry the same payout safely.</summary>
    public string IdempotencyKey { get; init; } = string.Empty;
}

/// <summary>Saved payout card (<c>GET /api/v1/cards</c>).</summary>
public sealed record SavedCard
{
    /// <summary>Card id to pass as <see cref="CardPayoutRequest.CardId"/>.</summary>
    public required string CardId { get; init; }

    /// <summary>Masked card number.</summary>
    public string? Masked { get; init; }

    /// <summary>Last four digits.</summary>
    public string? Last4 { get; init; }

    /// <summary>Card brand or bank.</summary>
    public string? Brand { get; init; }

    /// <summary>Label set in the merchant cabinet.</summary>
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

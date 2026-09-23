namespace Platega.Refunds;

/// <summary>Result of <c>GET /transaction/{id}/cancel-supported</c>.</summary>
public sealed record CancelAvailability
{
    /// <summary>True when the cancellation is possible and the merchant balance covers it.</summary>
    public bool Supported { get; init; }

    /// <summary>Total amount in USDT that will be deducted from the balance.</summary>
    public decimal? TotalDeductUsdt { get; init; }

    public decimal? PenaltyNativeAmount { get; init; }

    /// <summary>Penalty currency (RUB, EUR, ...).</summary>
    public string? PenaltyNativeCurrency { get; init; }

    public decimal? PenaltyUsdt { get; init; }

    /// <summary>Conversion rate applied to the penalty.</summary>
    public decimal? PenaltyConversionRate { get; init; }

    /// <summary>Why the cancellation is blocked, e.g. <c>Insufficient funds</c>.</summary>
    public string? BlockReason { get; init; }
}

/// <summary>Result of <c>POST /transaction/{id}/cancel</c>.</summary>
public sealed record CancelResult
{
    public Guid TransactionId { get; init; }

    /// <summary>True when the cancellation was accepted for automatic processing.</summary>
    public bool Accepted { get; init; }

    /// <summary>True when the cancellation needs manual handling by Platega support.</summary>
    public bool ManualControlRequired { get; init; }

    public string? Message { get; init; }
}

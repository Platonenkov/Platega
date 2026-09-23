using System.Text.Json.Serialization;
using Platega.Serialization;

namespace Platega.Payments;

internal sealed record CreateTransactionWireRequest
{
    public PaymentMethod? PaymentMethod { get; init; }

    public required Money PaymentDetails { get; init; }

    public required string Description { get; init; }

    [JsonPropertyName("return")]
    public required string Return { get; init; }

    public required string FailedUrl { get; init; }

    public string? Payload { get; init; }

    public string? OrderId { get; init; }

    public PayerMetadata? Metadata { get; init; }
}

internal sealed record CreateTransactionV2WireResponse
{
    public Guid TransactionId { get; init; }

    public PaymentStatus Status { get; init; }

    public string? Url { get; init; }

    [JsonConverter(typeof(TolerantTimeSpanConverter))]
    public TimeSpan? ExpiresIn { get; init; }

    public decimal? Rate { get; init; }
}

internal sealed record CreateTransactionWireResponse
{
    public string? PaymentMethod { get; init; }

    public Guid TransactionId { get; init; }

    public string? Redirect { get; init; }

    [JsonPropertyName("return")]
    public string? Return { get; init; }

    [JsonConverter(typeof(FlexibleMoneyConverter))]
    public Money? PaymentDetails { get; init; }

    public PaymentStatus Status { get; init; }

    [JsonConverter(typeof(TolerantTimeSpanConverter))]
    public TimeSpan? ExpiresIn { get; init; }

    public string? MerchantId { get; init; }

    public decimal? UsdtRate { get; init; }
}

internal sealed record TransactionExportWireRequest
{
    public required IReadOnlyList<string> Statuses { get; init; }

    public required IReadOnlyList<string> PaymentMethods { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public required string TimeZoneId { get; init; }
}

internal sealed record FileUrlWireResponse
{
    public string? Url { get; init; }
}

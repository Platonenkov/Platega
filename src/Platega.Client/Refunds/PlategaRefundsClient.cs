using Platega.Http;
using Platega.Serialization;

namespace Platega.Refunds;

/// <summary>Transaction cancellation and refunds.</summary>
public interface IPlategaRefundsClient
{
    /// <summary>Checks whether a transaction can be cancelled and how much will be deducted from the balance.</summary>
    Task<CancelAvailability> GetCancelAvailabilityAsync(Guid transactionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a transaction and refunds the payer. Check <see cref="GetCancelAvailabilityAsync"/> first.</summary>
    Task<CancelResult> CancelTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);
}

internal sealed class PlategaRefundsClient(PlategaConnection connection) : IPlategaRefundsClient
{
    public Task<CancelAvailability> GetCancelAvailabilityAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        connection.GetAsync(
            $"transaction/{PlategaFormat.FormatId(transactionId)}/cancel-supported",
            PlategaJsonContext.Relaxed.CancelAvailability,
            cancellationToken);

    public Task<CancelResult> CancelTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        connection.PostAsync(
            $"transaction/{PlategaFormat.FormatId(transactionId)}/cancel",
            PlategaJsonContext.Relaxed.CancelResult,
            cancellationToken);
}

using Platega.Http;
using Platega.Serialization;

namespace Platega.Balances;

/// <summary>Merchant balances.</summary>
public interface IPlategaBalancesClient
{
    /// <summary>Returns balances in every currency.</summary>
    Task<IReadOnlyList<PlategaBalance>> GetBalancesAsync(CancellationToken cancellationToken = default);
}

internal sealed class PlategaBalancesClient(PlategaConnection connection) : IPlategaBalancesClient
{
    public async Task<IReadOnlyList<PlategaBalance>> GetBalancesAsync(CancellationToken cancellationToken = default) =>
        await connection
            .GetAsync("balance/all", PlategaJsonContext.Relaxed.ListPlategaBalance, cancellationToken)
            .ConfigureAwait(false);
}

using Platega.Balances;
using Platega.Payments;
using Platega.Payouts;
using Platega.Refunds;
using Platega.Subscriptions;

namespace Platega;

/// <summary>Entry point grouping all Platega API areas.</summary>
public interface IPlategaClient
{
    IPlategaPaymentsClient Payments { get; }

    IPlategaRefundsClient Refunds { get; }

    IPlategaBalancesClient Balances { get; }

    IPlategaSubscriptionsClient Subscriptions { get; }

    IPlategaPayoutsClient Payouts { get; }
}

internal sealed class PlategaClient(
    IPlategaPaymentsClient payments,
    IPlategaRefundsClient refunds,
    IPlategaBalancesClient balances,
    IPlategaSubscriptionsClient subscriptions,
    IPlategaPayoutsClient payouts) : IPlategaClient
{
    public IPlategaPaymentsClient Payments { get; } = payments;

    public IPlategaRefundsClient Refunds { get; } = refunds;

    public IPlategaBalancesClient Balances { get; } = balances;

    public IPlategaSubscriptionsClient Subscriptions { get; } = subscriptions;

    public IPlategaPayoutsClient Payouts { get; } = payouts;
}

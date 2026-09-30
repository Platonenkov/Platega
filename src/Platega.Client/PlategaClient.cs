using Platega.Balances;
using Platega.Payments;
using Platega.Payouts;
using Platega.Refunds;
using Platega.Subscriptions;

namespace Platega;

/// <summary>Entry point grouping all Platega API areas.</summary>
public interface IPlategaClient
{
    /// <summary>Payments, status, H2H and exports.</summary>
    IPlategaPaymentsClient Payments { get; }

    /// <summary>Transaction cancellation and refunds.</summary>
    IPlategaRefundsClient Refunds { get; }

    /// <summary>Merchant balances.</summary>
    IPlategaBalancesClient Balances { get; }

    /// <summary>Recurring SBP subscriptions.</summary>
    IPlategaSubscriptionsClient Subscriptions { get; }

    /// <summary>Payout API (enabled per merchant on request).</summary>
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

using Platega.Balances;
using Platega.Refunds;
using Platega.Tests.Infrastructure;

namespace Platega.Tests;

public sealed class RefundsAndBalancesClientTests : IDisposable
{
    private readonly PlategaTestHost _host = new PlategaTestHost();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task GetCancelAvailability_ReadsNullablePenaltyFields()
    {
        _host.Handler.RespondWithFixture("cancel-supported.json");
        Guid id = Guid.NewGuid();

        CancelAvailability availability = await _host.Client.Refunds.GetCancelAvailabilityAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, _host.Handler.LastRequest.Method);
        Assert.Equal($"https://api.test/transaction/{id}/cancel-supported", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Contains("application/json", _host.Handler.LastRequest.Header("Accept"), StringComparison.Ordinal);
        Assert.True(availability.Supported);
        Assert.Equal(0.01236094m, availability.TotalDeductUsdt);
        Assert.Null(availability.PenaltyUsdt);
        Assert.Null(availability.BlockReason);
    }

    [Fact]
    public async Task CancelTransaction_PostsWithoutBody()
    {
        _host.Handler.RespondWithFixture("cancel.json");
        Guid id = Guid.Parse("71f1375c-ba7a-4e9d-84a5-452f3f9cf4c3");

        CancelResult result = await _host.Client.Refunds.CancelTransactionAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, _host.Handler.LastRequest.Method);
        Assert.Equal($"https://api.test/transaction/{id}/cancel", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Empty(_host.Handler.LastRequest.Body);
        Assert.Equal(id, result.TransactionId);
        Assert.False(result.Accepted);
        Assert.True(result.ManualControlRequired);
        Assert.Equal("Возврат в процессе", result.Message);
    }

    [Fact]
    public async Task GetBalances_DefaultsMissingFrozenBalanceToZero()
    {
        _host.Handler.RespondWithFixture("balances.json");

        IReadOnlyList<PlategaBalance> balances = await _host.Client.Balances.GetBalancesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://api.test/balance/all", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Collection(
            balances,
            rub =>
            {
                Assert.Equal("RUB", rub.Currency);
                Assert.Equal(15000.5m, rub.Amount);
                Assert.Equal(0m, rub.FrozenBalance);
            },
            usdt =>
            {
                Assert.Equal("USDT", usdt.Currency);
                Assert.Equal(500m, usdt.FrozenBalance);
            });
    }
}

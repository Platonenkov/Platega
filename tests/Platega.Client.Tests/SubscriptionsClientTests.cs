using System.Text.Json;
using Platega.Subscriptions;
using Platega.Tests.Infrastructure;

namespace Platega.Tests;

public sealed class SubscriptionsClientTests : IDisposable
{
    private readonly PlategaTestHost _host = new PlategaTestHost();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Create_SendsSubscriptionMethodAndNumericInterval()
    {
        _host.Handler.RespondWithFixture("subscription-create.json");

        CreatedSubscription created = await _host.Client.Subscriptions.CreateAsync(
            new CreateSubscriptionRequest
            {
                Amount = 500,
                Interval = SubscriptionInterval.Month,
                IntervalCount = 1,
                Description = "Premium подписка",
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("https://api.test/transaction/process", _host.Handler.LastRequest.Uri.AbsoluteUri);
        using JsonDocument body = JsonDocument.Parse(_host.Handler.LastRequest.Body);
        Assert.Equal(6, body.RootElement.GetProperty("paymentMethod").GetInt32());
        JsonElement details = body.RootElement.GetProperty("paymentDetails");
        Assert.Equal(500, details.GetProperty("amount").GetInt32());
        Assert.Equal("RUB", details.GetProperty("currency").GetString());
        Assert.Equal(3, details.GetProperty("interval").GetInt32());
        Assert.Equal(1, details.GetProperty("intervalCount").GetInt32());

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), created.SubscriptionId);
        Assert.StartsWith("https://pay.platega.io/subscription/", created.RedirectUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_RejectsNonHttpBindingLink()
    {
        _host.Handler.Respond(System.Net.HttpStatusCode.OK, "{\"transactionId\":\"11111111-1111-1111-1111-111111111111\",\"redirect\":\"javascript:alert(1)\",\"status\":\"PENDING\"}");

        await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Subscriptions.CreateAsync(
                new CreateSubscriptionRequest { Amount = 100, Interval = SubscriptionInterval.Month, Description = "x" },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(SubscriptionInterval.Day, 32)]
    [InlineData(SubscriptionInterval.Week, 5)]
    [InlineData(SubscriptionInterval.Month, 13)]
    [InlineData(SubscriptionInterval.Year, 4)]
    [InlineData(SubscriptionInterval.Month, 0)]
    public async Task Create_RejectsIntervalCountOutsideDocumentedLimits(SubscriptionInterval interval, int count)
    {
        CreateSubscriptionRequest request = new CreateSubscriptionRequest
        {
            Amount = 100,
            Interval = interval,
            IntervalCount = count,
            Description = "x",
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _host.Client.Subscriptions.CreateAsync(request, TestContext.Current.CancellationToken));
        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public async Task Get_ReadsStatusAndIntervalAsNames()
    {
        _host.Handler.RespondWithFixture("subscription.json");
        Guid id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        PlategaSubscription subscription = await _host.Client.Subscriptions.GetAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal($"https://api.test/subscription/{id}", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(SubscriptionInterval.Month, subscription.IntervalUnit);
        Assert.Equal("payer@example.com", subscription.CustomerEmail);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 9, 10, 0, TimeSpan.Zero), subscription.NextChargeAt);
        Assert.NotNull(subscription.ChargeMetrics);
        Assert.Equal(1, subscription.ChargeMetrics.ChargesSuccess);
    }

    [Fact]
    public async Task List_ReadsStatusAndIntervalAsCodesAndBuildsQuery()
    {
        _host.Handler.RespondWithFixture("subscription-list.json");

        SubscriptionPage page = await _host.Client.Subscriptions.ListAsync(
            new SubscriptionListFilter
            {
                Status = SubscriptionStatus.Active,
                From = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                Page = 2,
                Size = 10,
            },
            TestContext.Current.CancellationToken);

        Uri uri = _host.Handler.LastRequest.Uri;
        Assert.Equal("/subscription", uri.AbsolutePath);
        Assert.Equal("?page=2&size=10&status=1&from=2026-07-01T00%3A00%3A00.000Z", uri.Query);

        Assert.Equal(2, page.Total);
        Assert.Collection(
            page.Items,
            first =>
            {
                Assert.Equal(SubscriptionStatus.Failed, first.Status);
                Assert.Equal(SubscriptionInterval.Month, first.IntervalUnit);
                Assert.Null(first.CustomerEmail);
                Assert.Equal(0, first.ChargesCount);
            },
            second => Assert.Equal(SubscriptionInterval.Week, second.IntervalUnit));
    }

    [Fact]
    public async Task List_ReadsLiveEmptyPage()
    {
        _host.Handler.RespondWithFixture("subscription-list-empty.json");

        SubscriptionPage page = await _host.Client.Subscriptions.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Equal(50, page.Size);
    }

    [Fact]
    public async Task Cancel_PostsToCancelEndpoint()
    {
        _host.Handler.RespondWithFixture("subscription-cancel.json");
        Guid id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        SubscriptionCancelResult result = await _host.Client.Subscriptions.CancelAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, _host.Handler.LastRequest.Method);
        Assert.Equal($"https://api.test/subscription/{id}/cancel", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Equal(id, result.SubscriptionId);
        Assert.Equal("cancelled", result.Status);
    }
}

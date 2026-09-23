using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Platega.Balances;
using Platega.Payments;
using Platega.Payouts;
using Platega.Refunds;
using Platega.Subscriptions;

namespace Platega.Tests;

/// <summary>
/// Runs the real client against Platega.FakeServer, which emits the documented wire format independently of the client models.
/// </summary>
public sealed class FakeServerIntegrationTests : IClassFixture<FakeServerIntegrationTests.FakeServerFactory>, IDisposable
{
    private const string MerchantId = "00000000-0000-4000-8000-00000000fa6e";

    private readonly FakeServerFactory _factory;
    private readonly ServiceProvider _provider;

    public FakeServerIntegrationTests(FakeServerFactory factory)
    {
        _factory = factory;
        _provider = BuildClient("fake-api-secret");
    }

    private IPlategaClient Client => _provider.GetRequiredService<IPlategaClient>();

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Payment_PayRefund_Lifecycle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CreatedPayment created = await Client.Payments.CreatePaymentAsync(
            new CreatePaymentRequest
            {
                Amount = new Money(1000m, "RUB"),
                Description = "Интеграционный тест",
                ReturnUrl = new Uri("https://shop.test/ok"),
                FailedUrl = new Uri("https://shop.test/fail"),
                Payload = "order-7",
            },
            cancellationToken);

        Assert.Equal(PaymentStatus.Pending, created.Status);
        Assert.EndsWith($"/pay/{created.TransactionId}", created.PaymentUrl, StringComparison.Ordinal);
        Assert.InRange(created.ExpiresIn!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        using HttpResponseMessage payResponse = await PayAsync(created.TransactionId, method: "11", cancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, payResponse.StatusCode);
        Assert.Equal("https://shop.test/ok", payResponse.Headers.Location?.AbsoluteUri);

        PlategaTransaction paid = await Client.Payments.GetTransactionAsync(created.TransactionId, cancellationToken);
        Assert.Equal(PaymentStatus.Confirmed, paid.Status);
        Assert.Equal(new Money(1000m, "RUB"), paid.Amount);
        Assert.Equal("CARD", paid.PaymentMethod);
        Assert.Equal("order-7", paid.Payload);
        Assert.Equal(MerchantId, paid.MerchantId);

        CancelAvailability availability = await Client.Refunds.GetCancelAvailabilityAsync(created.TransactionId, cancellationToken);
        Assert.True(availability.Supported);
        Assert.True(availability.TotalDeductUsdt > 0);

        CancelResult cancelled = await Client.Refunds.CancelTransactionAsync(created.TransactionId, cancellationToken);
        Assert.True(cancelled.Accepted);

        PlategaTransaction refunded = await Client.Payments.GetTransactionAsync(created.TransactionId, cancellationToken);
        Assert.Equal(PaymentStatus.Chargebacked, refunded.Status);

        CancelAvailability again = await Client.Refunds.GetCancelAvailabilityAsync(created.TransactionId, cancellationToken);
        Assert.False(again.Supported);
        Assert.NotNull(again.BlockReason);
    }

    [Fact]
    public async Task Payment_WithFixedMethod_ReturnsMethodAndAmount()
    {
        CreatedPayment created = await Client.Payments.CreatePaymentAsync(
            new CreatePaymentRequest
            {
                Amount = new Money(250.5m, "RUB"),
                Description = "SberPay",
                ReturnUrl = new Uri("https://shop.test/ok"),
                FailedUrl = new Uri("https://shop.test/fail"),
                Method = PaymentMethod.SberPay,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal("SBERPAY", created.PaymentMethodName);
        Assert.Equal(new Money(250.5m, "RUB"), created.Amount);
        Assert.Equal(MerchantId, created.MerchantId);

        H2HPaymentData h2h = await Client.Payments.GetH2HPaymentDataAsync(created.TransactionId, TestContext.Current.CancellationToken);
        Assert.Equal(250.5m, h2h.Amount);
    }

    [Fact]
    public async Task Subscription_BindCancel_Lifecycle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CreatedSubscription created = await Client.Subscriptions.CreateAsync(
            new CreateSubscriptionRequest { Amount = 300, Interval = SubscriptionInterval.Week, IntervalCount = 2, Description = "Weekly" },
            cancellationToken);

        PlategaSubscription pending = await Client.Subscriptions.GetAsync(created.SubscriptionId, cancellationToken);
        Assert.Equal(SubscriptionStatus.PendingAgreement, pending.Status);

        using HttpClient browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using FormUrlEncodedContent form = new FormUrlEncodedContent(new Dictionary<string, string> { ["result"] = "confirm", ["email"] = "payer@test.io" });
        using HttpResponseMessage bind = await browser.PostAsync(new Uri(created.RedirectUrl).PathAndQuery, form, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, bind.StatusCode);

        PlategaSubscription active = await Client.Subscriptions.GetAsync(created.SubscriptionId, cancellationToken);
        Assert.Equal(SubscriptionStatus.Active, active.Status);
        Assert.Equal(SubscriptionInterval.Week, active.IntervalUnit);
        Assert.Equal("payer@test.io", active.CustomerEmail);
        Assert.Equal(1, active.ChargeMetrics?.ChargesSuccess);
        Assert.NotNull(active.NextChargeAt);

        SubscriptionPage page = await Client.Subscriptions.ListAsync(new SubscriptionListFilter { Status = SubscriptionStatus.Active, Size = 100 }, cancellationToken);
        PlategaSubscription listed = Assert.Single(page.Items, item => item.Id == created.SubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, listed.Status);
        Assert.Equal(SubscriptionInterval.Week, listed.IntervalUnit);

        SubscriptionCancelResult cancelled = await Client.Subscriptions.CancelAsync(created.SubscriptionId, cancellationToken);
        Assert.Equal(created.SubscriptionId, cancelled.SubscriptionId);
        Assert.Equal(SubscriptionStatus.Cancelled, (await Client.Subscriptions.GetAsync(created.SubscriptionId, cancellationToken)).Status);

        SubscriptionCancelResult cancelledAgain = await Client.Subscriptions.CancelAsync(created.SubscriptionId, cancellationToken);
        Assert.Equal("cancelled", cancelledAgain.Status);
    }

    [Fact]
    public async Task Payouts_AreSignedAndIdempotent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IReadOnlyList<SavedCard> cards = await Client.Payouts.GetSavedCardsAsync(onlyActive: false, cancellationToken);
        Assert.Equal(2, cards.Count);

        CardPayoutRequest request = new CardPayoutRequest { CardId = cards[0].CardId, AmountRub = 1500 };
        CardPayoutResult first = await Client.Payouts.CreateCardPayoutAsync(request, cancellationToken: cancellationToken);
        CardPayoutResult retry = await Client.Payouts.CreateCardPayoutAsync(request, first.IdempotencyKey, cancellationToken);

        Assert.Equal("CREATED", first.Status);
        Assert.Equal(first.WithdrawalRecordId, retry.WithdrawalRecordId);
        Assert.True(first.AmountUsdtDebited > 0);
    }

    [Fact]
    public async Task Payouts_WithWrongSecret_AreRejected()
    {
        await using ServiceProvider provider = BuildClient("fake-api-secret", payoutSecret: "wrong");

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => provider.GetRequiredService<IPlategaClient>().Payouts.GetSavedCardsAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task WrongApiSecret_IsRejected()
    {
        await using ServiceProvider provider = BuildClient("wrong-secret");

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => provider.GetRequiredService<IPlategaClient>().Balances.GetBalancesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task BalancesAndExport_Work()
    {
        IReadOnlyList<PlategaBalance> balances = await Client.Balances.GetBalancesAsync(TestContext.Current.CancellationToken);
        Assert.Contains(balances, balance => balance.Currency == "USDT");

        Uri file = await Client.Payments.ExportTransactionsAsync(
            new TransactionExportRequest { From = DateTimeOffset.UtcNow.AddDays(-1), To = DateTimeOffset.UtcNow.AddMinutes(1) },
            TransactionExportFormat.Csv,
            TestContext.Current.CancellationToken);

        using HttpClient browser = _factory.CreateClient();
        string csv = await browser.GetStringAsync(file.PathAndQuery, TestContext.Current.CancellationToken);
        Assert.StartsWith("id;createdAt;status", csv.TrimStart('﻿'), StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> PayAsync(Guid transactionId, string method, CancellationToken cancellationToken)
    {
        using HttpClient browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using FormUrlEncodedContent form = new FormUrlEncodedContent(new Dictionary<string, string> { ["result"] = "confirm", ["method"] = method });
        return await browser.PostAsync($"/pay/{transactionId}", form, cancellationToken);
    }

    private ServiceProvider BuildClient(string secret, string payoutSecret = "fake-payout-secret")
    {
        ServiceCollection services = new ServiceCollection();
        services.AddPlatega(options =>
        {
            options.MerchantId = Guid.Parse(MerchantId);
            options.Secret = secret;
            options.PayoutSecret = payoutSecret;
            options.BaseAddress = _factory.Server.BaseAddress;
        });
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = _factory.Server.CreateHandler()));
        return services.BuildServiceProvider();
    }

    public sealed class FakeServerFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Fake:CallbackUrl", string.Empty);
            builder.UseSetting("Fake:PublicBaseUrl", "http://localhost/");
        }
    }
}

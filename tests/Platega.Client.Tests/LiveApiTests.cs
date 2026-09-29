using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Platega.Balances;
using Platega.Payments;
using Platega.Payouts;
using Platega.Refunds;
using Platega.Subscriptions;

namespace Platega.Tests;

/// <summary>
/// Checks the client against the real Platega API. Skipped unless credentials are supplied:
/// <c>PLATEGA_MERCHANT_ID</c>, <c>PLATEGA_SECRET</c>, optional <c>PLATEGA_BASE_ADDRESS</c> and <c>PLATEGA_PAYOUT_SECRET</c>.
/// Read-only calls run whenever credentials are present; creating a payment additionally requires <c>PLATEGA_LIVE_CREATE=1</c>.
/// Raw responses are written to the test output to verify the wire format against the documentation.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveApiTests : IDisposable
{
    private readonly ServiceProvider? _provider;

    public LiveApiTests()
    {
        string? merchantId = Environment.GetEnvironmentVariable("PLATEGA_MERCHANT_ID");
        string? secret = Environment.GetEnvironmentVariable("PLATEGA_SECRET");
        if (!Guid.TryParse(merchantId, out Guid parsedMerchantId) || string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        ServiceCollection services = new ServiceCollection();
        services.AddPlatega(options =>
        {
            options.MerchantId = parsedMerchantId;
            options.Secret = secret;
            options.PayoutSecret = Environment.GetEnvironmentVariable("PLATEGA_PAYOUT_SECRET");
            if (Uri.TryCreate(Environment.GetEnvironmentVariable("PLATEGA_BASE_ADDRESS"), UriKind.Absolute, out Uri? baseAddress))
            {
                options.BaseAddress = baseAddress;
            }
        });
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.AdditionalHandlers.Add(new ResponseLoggingHandler())));
        _provider = services.BuildServiceProvider();
    }

    private IPlategaClient Client
    {
        get
        {
            Assert.SkipWhen(_provider is null, "Set PLATEGA_MERCHANT_ID and PLATEGA_SECRET to run live API tests.");
            return _provider!.GetRequiredService<IPlategaClient>();
        }
    }

    public void Dispose() => _provider?.Dispose();

    [Fact]
    public async Task Balances_AreReadable()
    {
        IReadOnlyList<PlategaBalance> balances = await Client.Balances.GetBalancesAsync(TestContext.Current.CancellationToken);

        Assert.All(balances, balance => Assert.False(string.IsNullOrWhiteSpace(balance.Currency)));
    }

    [Fact]
    public async Task Subscriptions_ListUsesKnownStatusCodes()
    {
        SubscriptionPage page = await Client.Subscriptions.ListAsync(new SubscriptionListFilter { Size = 50 }, TestContext.Current.CancellationToken);

        Assert.True(page.Total >= page.Items.Count);
        Assert.All(page.Items, item => Assert.NotEqual(SubscriptionStatus.Unknown, item.Status));
        Assert.All(page.Items, item => Assert.NotEqual(SubscriptionInterval.Unknown, item.IntervalUnit));

        if (page.Items.Count > 0)
        {
            // Compares the numeric status of the list with the named status of the single-item endpoint.
            PlategaSubscription listed = page.Items[0];
            PlategaSubscription single = await Client.Subscriptions.GetAsync(listed.Id, TestContext.Current.CancellationToken);
            Assert.Equal(single.Status, listed.Status);
            Assert.Equal(single.IntervalUnit, listed.IntervalUnit);
        }
    }

    [Fact]
    public async Task UnknownTransaction_IsReportedAsApiError()
    {
        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        TestContext.Current.TestOutputHelper?.WriteLine($"Unknown transaction -> {(int)exception.StatusCode}: {exception.ResponseBody}");
        Assert.NotEqual(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task SavedCards_AcceptPayoutSignature()
    {
        IPlategaClient client = Client;
        Assert.SkipUnless(client.Payouts.IsConfigured, "Set PLATEGA_PAYOUT_SECRET to check the Payout API signature.");

        IReadOnlyList<SavedCard> cards = await client.Payouts.GetSavedCardsAsync(onlyActive: false, TestContext.Current.CancellationToken);

        Assert.All(cards, card => Assert.False(string.IsNullOrWhiteSpace(card.CardId)));
    }

    [Fact]
    public async Task Payment_CanBeCreatedAndRead()
    {
        IPlategaClient client = Client;
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("PLATEGA_LIVE_CREATE") == "1",
            "Set PLATEGA_LIVE_CREATE=1 to create a real 100 RUB payment link (no money moves unless someone pays it).");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        CreatedPayment created = await client.Payments.CreatePaymentAsync(
            new CreatePaymentRequest
            {
                Amount = new Money(100m, "RUB"),
                Description = "Platega.Client live test",
                ReturnUrl = new Uri("https://example.com/platega/success"),
                FailedUrl = new Uri("https://example.com/platega/fail"),
                Payload = "live-test",
                OrderId = $"live-{Guid.NewGuid():N}",
            },
            cancellationToken);

        Assert.Equal(PaymentStatus.Pending, created.Status);
        Assert.True(Uri.TryCreate(created.PaymentUrl, UriKind.Absolute, out _), created.PaymentUrl);

        PlategaTransaction transaction = await client.Payments.GetTransactionAsync(created.TransactionId, cancellationToken);
        Assert.Equal(created.TransactionId, transaction.Id);
        Assert.Equal(PaymentStatus.Pending, transaction.Status);
        Assert.Equal("live-test", transaction.Payload);

        CancelAvailability availability = await client.Refunds.GetCancelAvailabilityAsync(created.TransactionId, cancellationToken);
        Assert.False(availability.Supported);
    }

    [Fact]
    public async Task Subscription_NumericListStatusMatchesNamedStatus()
    {
        IPlategaClient client = Client;
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("PLATEGA_LIVE_CREATE") == "1",
            "Set PLATEGA_LIVE_CREATE=1 to create a real subscription awaiting binding (it turns Failed after 30 minutes unless bound).");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        CreatedSubscription created;
        try
        {
            created = await client.Subscriptions.CreateAsync(
                new CreateSubscriptionRequest
                {
                    Amount = 100,
                    Interval = SubscriptionInterval.Month,
                    IntervalCount = 1,
                    Description = "Platega.Client live test",
                },
                cancellationToken);
        }
        catch (PlategaApiException exception) when (exception.ErrorDetails.Any(detail => detail.Key == "paymentMethod"))
        {
            Assert.Skip($"SBP subscriptions are not enabled for this merchant: {exception.ErrorCode} {exception.ErrorMessage}");
            return;
        }

        PlategaSubscription single = await client.Subscriptions.GetAsync(created.SubscriptionId, cancellationToken);
        SubscriptionPage page = await client.Subscriptions.ListAsync(new SubscriptionListFilter { Size = 100 }, cancellationToken);
        PlategaSubscription listed = Assert.Single(page.Items, item => item.Id == created.SubscriptionId);

        Assert.NotEqual(SubscriptionStatus.Unknown, single.Status);
        Assert.Equal(single.Status, listed.Status);
        Assert.Equal(SubscriptionInterval.Month, single.IntervalUnit);
        Assert.Equal(SubscriptionInterval.Month, listed.IntervalUnit);

        await client.Subscriptions.CancelAsync(created.SubscriptionId, cancellationToken);
    }

    /// <summary>
    /// Writes each response (never the request headers, which carry the secret) to the test output and appends it
    /// to <c>live-responses.log</c> next to the test binaries, since passing tests do not print their output.
    /// </summary>
    private sealed class ResponseLoggingHandler : DelegatingHandler
    {
        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "live-responses.log");
        private static readonly Lock LogSync = new Lock();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            string entry = $"{DateTimeOffset.UtcNow:O} {request.Method} {request.RequestUri?.PathAndQuery} -> {(int)response.StatusCode} {response.Content.Headers.ContentType}\n{body}\n";
            TestContext.Current.TestOutputHelper?.WriteLine(entry);
            lock (LogSync)
            {
                File.AppendAllText(LogPath, entry + "\n");
            }

            return response;
        }
    }
}

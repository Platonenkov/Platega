using Platega.Http;
using Platega.Payouts;
using Platega.Tests.Infrastructure;

namespace Platega.Tests;

/// <summary>
/// Reference signatures were produced by the Python sample from the Platega documentation
/// with secret <c>test-payout-secret</c>, timestamp <c>1719403200</c> and the bodies below.
/// </summary>
public sealed class PayoutsClientTests
{
    private const string IdempotencyKey = "00000000-0000-0000-0000-446655440000";
    private const string ExpectedBody = "{\"cardNumber\":\"2200000000000000\",\"amountRub\":1500,\"payoutMethod\":\"CARD\",\"currencyRequested\":\"RUB\"}";
    private const string ExpectedPayoutSignature = "zy6tRCuhUDpkn5/9qWFHoiQegiISjjx/pWCzfM+aQ4c=";
    private const string ExpectedCardsSignature = "2OAj72i/xoYxuZcNf4y9pidBh9k3XMyHJG1ZaSV0AWs=";

    [Fact]
    public async Task CreateCardPayout_SignsExactBodyBytesLikeReferenceImplementation()
    {
        using PlategaTestHost host = new PlategaTestHost();
        host.Handler.RespondWithFixture("payout.json");

        CardPayoutResult result = await host.Client.Payouts.CreateCardPayoutAsync(
            new CardPayoutRequest { CardNumber = "2200000000000000", AmountRub = 1500 },
            IdempotencyKey,
            TestContext.Current.CancellationToken);

        RecordedRequest request = host.Handler.LastRequest;
        Assert.Equal("https://api.test/api/v1/payouts/card-rub", request.Uri.AbsoluteUri);
        Assert.Equal(ExpectedBody, request.BodyText);
        Assert.Equal(IdempotencyKey, request.Header("Idempotency-Key"));
        Assert.Equal(
            $"PG-HMAC kid={PlategaTestHost.MerchantId}, ts=1719403200, sig={ExpectedPayoutSignature}",
            request.Header("Authorization"));
        Assert.Null(request.Header("X-Secret"));

        Assert.Equal(Guid.Parse("3c0d321d-40c4-46e3-97f0-7a8f50ce03a6"), result.WithdrawalRecordId);
        Assert.Equal("CREATED", result.Status);
        Assert.Equal(13.270341m, result.AmountUsdtDebited);
        Assert.Equal(IdempotencyKey, result.IdempotencyKey);
    }

    [Fact]
    public async Task CreateCardPayout_GeneratesIdempotencyKeyWhenOmitted()
    {
        using PlategaTestHost host = new PlategaTestHost();
        host.Handler.RespondWithFixture("payout.json");

        CardPayoutResult result = await host.Client.Payouts.CreateCardPayoutAsync(
            new CardPayoutRequest { CardId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890", AmountRub = 1000 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(Guid.TryParse(result.IdempotencyKey, out _));
        Assert.Equal(result.IdempotencyKey, host.Handler.LastRequest.Header("Idempotency-Key"));
        Assert.DoesNotContain("cardNumber", host.Handler.LastRequest.BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSavedCards_SignsGetWithEmptyKeyAndEmptyBodyHash()
    {
        using PlategaTestHost host = new PlategaTestHost();
        host.Handler.RespondWithFixture("cards.json");

        IReadOnlyList<SavedCard> cards = await host.Client.Payouts.GetSavedCardsAsync(cancellationToken: TestContext.Current.CancellationToken);

        RecordedRequest request = host.Handler.LastRequest;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.test/api/v1/cards", request.Uri.AbsoluteUri);
        Assert.EndsWith($"sig={ExpectedCardsSignature}", request.Header("Authorization"), StringComparison.Ordinal);
        Assert.Equal(2, cards.Count);
        Assert.Equal("4242", cards[0].Last4);
        Assert.Equal("DISABLED", cards[1].Status);
    }

    [Fact]
    public async Task GetSavedCards_PassesOnlyActiveFlag()
    {
        using PlategaTestHost host = new PlategaTestHost();
        host.Handler.RespondWithFixture("cards.json");

        await host.Client.Payouts.GetSavedCardsAsync(onlyActive: false, TestContext.Current.CancellationToken);

        Assert.Equal("?onlyActive=false", host.Handler.LastRequest.Uri.Query);
    }

    [Fact]
    public async Task Payouts_RequireConfiguredSecret()
    {
        using PlategaTestHost host = new PlategaTestHost(withPayoutSecret: false);

        Assert.False(host.Client.Payouts.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Client.Payouts.GetSavedCardsAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(host.Handler.Requests);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(87501)]
    public async Task CreateCardPayout_EnforcesAmountRange(int amount)
    {
        using PlategaTestHost host = new PlategaTestHost();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => host.Client.Payouts.CreateCardPayoutAsync(
                new CardPayoutRequest { CardNumber = "2200000000000000", AmountRub = amount },
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("card-id", "2200000000000000")]
    [InlineData(null, "2200 0000 0000 0000")]
    public async Task CreateCardPayout_RequiresExactlyOneValidCard(string? cardId, string? cardNumber)
    {
        using PlategaTestHost host = new PlategaTestHost();

        await Assert.ThrowsAsync<ArgumentException>(
            () => host.Client.Payouts.CreateCardPayoutAsync(
                new CardPayoutRequest { CardId = cardId, CardNumber = cardNumber, AmountRub = 1500 },
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Signer_EmptyBodyHashMatchesDocumentedConstant()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            PlategaHmacSigner.Sha256Hex([]));
    }

    [Fact]
    public void Signer_StringToSignFollowsDocumentedLayout()
    {
        string stringToSign = PlategaHmacSigner.BuildStringToSign("post", "/api/v1/payouts/card-rub", 1719403200, "key", "{}"u8);

        Assert.Equal(
            "POST\n/api/v1/payouts/card-rub\n1719403200\nkey\n44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
            stringToSign);
    }
}

using Microsoft.Extensions.Options;
using Platega.Callbacks;
using Platega.Subscriptions;
using Platega.Tests.Infrastructure;

namespace Platega.Tests;

public sealed class CallbackParserTests
{
    private readonly PlategaCallbackParser _parser = new PlategaCallbackParser(new StaticOptionsMonitor(new PlategaOptions
    {
        MerchantId = PlategaTestHost.MerchantId,
        Secret = PlategaTestHost.Secret,
    }));

    [Fact]
    public void IsAuthentic_AcceptsMatchingHeaders() =>
        Assert.True(_parser.IsAuthentic(PlategaTestHost.MerchantId.ToString().ToUpperInvariant(), PlategaTestHost.Secret));

    [Theory]
    [InlineData(null, PlategaTestHost.Secret)]
    [InlineData("not-a-guid", PlategaTestHost.Secret)]
    [InlineData("11111111-1111-1111-1111-111111111111", PlategaTestHost.Secret)]
    [InlineData("29ef0000-0000-0000-0000-000000000001", null)]
    [InlineData("29ef0000-0000-0000-0000-000000000001", "")]
    [InlineData("29ef0000-0000-0000-0000-000000000001", "test-api-secreT")]
    public void IsAuthentic_RejectsWrongHeaders(string? merchantId, string? secret) =>
        Assert.False(_parser.IsAuthentic(merchantId, secret));

    [Fact]
    public void TryParse_PaymentCallbackInCamelCase()
    {
        bool parsed = PlategaCallbackParser.TryParse(Fixture.ReadBytes("callback-payment.json"), out PlategaCallback? callback, out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(callback);
        Assert.Equal(PlategaCallbackKind.Payment, callback.Kind);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), callback.Id);
        Assert.Equal(PaymentStatus.Confirmed, callback.PaymentStatus);
        Assert.Equal(PaymentMethod.SbpQr, callback.PaymentMethod);
        Assert.Equal(1000m, callback.Amount);
        Assert.Equal("RUB", callback.Currency);
        Assert.Equal("order-42", callback.Payload);
        Assert.Null(callback.SubscriptionId);
        Assert.Contains("order-42", callback.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_SubscriptionChargeInPascalCase()
    {
        bool parsed = PlategaCallbackParser.TryParse(Fixture.ReadBytes("callback-subscription-charge.json"), out PlategaCallback? callback, out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(callback);
        Assert.Equal(PlategaCallbackKind.SubscriptionCharge, callback.Kind);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), callback.Id);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), callback.SubscriptionId);
        Assert.Equal(PaymentStatus.Canceled, callback.PaymentStatus);
        Assert.Equal(PaymentMethod.Subscription, callback.PaymentMethod);
        Assert.Null(callback.NextChargeAt);
    }

    [Fact]
    public void TryParse_SubscriptionStatusChange()
    {
        bool parsed = PlategaCallbackParser.TryParse(Fixture.ReadBytes("callback-subscription-status.json"), out PlategaCallback? callback, out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(callback);
        Assert.Equal(PlategaCallbackKind.SubscriptionStatusChanged, callback.Kind);
        Assert.Equal(SubscriptionCallbackStatus.Activated, callback.SubscriptionStatus);
        Assert.Equal(PaymentStatus.Unknown, callback.PaymentStatus);
        Assert.Equal(callback.Id, callback.SubscriptionId);
        Assert.Equal(new DateTimeOffset(2026, 8, 9, 9, 10, 0, TimeSpan.Zero), callback.NextChargeAt);
    }

    [Fact]
    public void TryParse_KeepsUnknownStatusRaw()
    {
        bool parsed = PlategaCallbackParser.TryParse(
            "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"status\":\"CHARGEBACKED\",\"paymentMethod\":99}"u8,
            out PlategaCallback? callback,
            out _);

        Assert.True(parsed);
        Assert.NotNull(callback);
        Assert.Equal(PaymentStatus.Chargebacked, callback.PaymentStatus);
        Assert.Equal((PaymentMethod)99, callback.PaymentMethod);
    }

    [Fact]
    public void TryParse_ToleratesMalformedOptionalDate()
    {
        bool parsed = PlategaCallbackParser.TryParse(
            "{\"Id\":\"33333333-3333-3333-3333-333333333333\",\"Status\":\"CONFIRMED\",\"SubscriptionId\":\"11111111-1111-1111-1111-111111111111\",\"NextChargeAt\":\"\"}"u8,
            out PlategaCallback? callback,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(callback);
        Assert.Equal(PlategaCallbackKind.SubscriptionCharge, callback.Kind);
        Assert.Null(callback.NextChargeAt);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"id\":\"42\",\"status\":\"CONFIRMED\"}")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("null")]
    public void TryParse_RejectsInvalidBodies(string body)
    {
        bool parsed = PlategaCallbackParser.TryParse(System.Text.Encoding.UTF8.GetBytes(body), out PlategaCallback? callback, out string? error);

        Assert.False(parsed);
        Assert.Null(callback);
        Assert.False(string.IsNullOrEmpty(error));
    }

    private sealed class StaticOptionsMonitor(PlategaOptions value) : IOptionsMonitor<PlategaOptions>
    {
        public PlategaOptions CurrentValue => value;

        public PlategaOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<PlategaOptions, string?> listener) => null;
    }
}

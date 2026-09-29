using System.Net;
using System.Text.Json;
using Platega.Payments;
using Platega.Tests.Infrastructure;

namespace Platega.Tests;

public sealed class PaymentsClientTests : IDisposable
{
    private readonly PlategaTestHost _host = new PlategaTestHost();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task CreatePayment_WithoutMethod_UsesV2EndpointAndAuthHeaders()
    {
        _host.Handler.RespondWithFixture("create-v2.json");

        CreatedPayment created = await _host.Client.Payments.CreatePaymentAsync(NewRequest(method: null), TestContext.Current.CancellationToken);

        RecordedRequest request = _host.Handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.test/v2/transaction/process", request.Uri.AbsoluteUri);
        Assert.Equal(PlategaTestHost.MerchantId.ToString(), request.Header("X-MerchantId"));
        Assert.Equal(PlategaTestHost.Secret, request.Header("X-Secret"));
        Assert.Equal("application/json", request.ContentType);

        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.False(body.RootElement.TryGetProperty("paymentMethod", out _));
        Assert.Equal(500m, body.RootElement.GetProperty("paymentDetails").GetProperty("amount").GetDecimal());
        Assert.Equal("RUB", body.RootElement.GetProperty("paymentDetails").GetProperty("currency").GetString());
        Assert.Equal("https://shop.test/ok", body.RootElement.GetProperty("return").GetString());
        Assert.Equal("https://shop.test/fail", body.RootElement.GetProperty("failedUrl").GetString());
        Assert.Equal("order-1", body.RootElement.GetProperty("orderId").GetString());
        Assert.Equal("42", body.RootElement.GetProperty("metadata").GetProperty("userId").GetString());
        Assert.Contains("Оплата заказа", request.BodyText, StringComparison.Ordinal);

        Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), created.TransactionId);
        Assert.Equal(PaymentStatus.Pending, created.Status);
        Assert.StartsWith("https://pay.platega.io/?id=", created.PaymentUrl, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(15), created.ExpiresIn);
        Assert.Equal(91.2m, created.UsdtRate);
    }

    [Fact]
    public async Task CreatePayment_WithMethod_SendsNumericMethodAndParsesTextAmount()
    {
        _host.Handler.RespondWithFixture("create-with-method.json");

        CreatedPayment created = await _host.Client.Payments.CreatePaymentAsync(NewRequest(PaymentMethod.SbpQr), TestContext.Current.CancellationToken);

        RecordedRequest request = _host.Handler.LastRequest;
        Assert.Equal("https://api.test/transaction/process", request.Uri.AbsoluteUri);
        using JsonDocument body = JsonDocument.Parse(request.Body);
        Assert.Equal(2, body.RootElement.GetProperty("paymentMethod").GetInt32());

        Assert.Equal("https://pay.platega.io?qrsbp", created.PaymentUrl);
        Assert.Equal("SBPQR", created.PaymentMethodName);
        Assert.Equal(new Money(100m, "RUB"), created.Amount);
        Assert.Equal(93.45m, created.UsdtRate);
    }

    [Fact]
    public async Task GetTransaction_MapsDocumentedFieldsIncludingMisspelledOnes()
    {
        _host.Handler.RespondWithFixture("transaction.json");
        Guid id = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

        PlategaTransaction transaction = await _host.Client.Payments.GetTransactionAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, _host.Handler.LastRequest.Method);
        Assert.Equal($"https://api.test/transaction/{id}", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Equal(id, transaction.Id);
        Assert.Equal(PaymentStatus.Pending, transaction.Status);
        Assert.Equal(new Money(2000m, "RUB"), transaction.Amount);
        Assert.Equal("3fa85f64-5717-4562-b3fc-2c963f66afa6", transaction.MerchantId);
        Assert.Equal(1.64044944m, transaction.CommissionUsdt);
        Assert.Equal(1, transaction.CommissionType);
        Assert.Equal("https://example.com/success", transaction.ReturnUrl);
        Assert.Equal("custom-payload", transaction.Payload);
        Assert.Equal("Оплата заказа #12345", transaction.Description);
    }

    [Fact]
    public async Task CreatePayment_ReadsLiveV2Response()
    {
        _host.Handler.RespondWithFixture("create-v2-live.json");

        CreatedPayment created = await _host.Client.Payments.CreatePaymentAsync(NewRequest(null), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentStatus.Pending, created.Status);
        Assert.StartsWith("https://pay.platega.io/p/", created.PaymentUrl, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(30), created.ExpiresIn);
        Assert.Equal(0m, created.UsdtRate);
    }

    [Fact]
    public async Task GetTransaction_ReadsLivePendingResponse()
    {
        _host.Handler.RespondWithFixture("transaction-pending-live.json");

        PlategaTransaction transaction = await _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentStatus.Pending, transaction.Status);
        Assert.Equal(new Money(100m, "RUB"), transaction.Amount);
        Assert.Null(transaction.PaymentMethod);
        Assert.Null(transaction.Qr);
        Assert.Null(transaction.RefundStatus);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 11, 39, 34, 489, TimeSpan.Zero).AddTicks(5950), transaction.CreatedAt);
        Assert.Equal("00000000-0000-0000-0000-00000000b001", transaction.MerchantId);
    }

    [Fact]
    public async Task ValidationErrorBody_ExposesRejectedParameter()
    {
        _host.Handler.Respond(HttpStatusCode.BadRequest, Fixture.Read("error-400-subscription.json"));

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Payments.CreatePaymentAsync(NewRequest(null), TestContext.Current.CancellationToken));

        Assert.Equal("Common:VAL_0001", exception.ErrorCode);
        Assert.Equal(4001, exception.ErrorType);
        PlategaErrorDetail detail = Assert.Single(exception.ErrorDetails);
        Assert.Equal("paymentMethod", detail.Key);
    }

    [Fact]
    public async Task GetH2HPaymentData_ReturnsQr()
    {
        _host.Handler.RespondWithFixture("h2h.json");
        Guid id = Guid.NewGuid();

        H2HPaymentData data = await _host.Client.Payments.GetH2HPaymentDataAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal($"https://api.test/h2h/{id}", _host.Handler.LastRequest.Uri.AbsoluteUri);
        Assert.Equal(136.12m, data.Amount);
        Assert.StartsWith("https://qr.nspk.ru/", data.Qr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TransactionExportFormat.Csv, "csv")]
    [InlineData(TransactionExportFormat.Excel, "excel")]
    [InlineData(TransactionExportFormat.Json, "json")]
    public async Task ExportTransactions_PostsFiltersInApiFormat(TransactionExportFormat format, string segment)
    {
        _host.Handler.RespondWithFixture("export.json");
        TransactionExportRequest request = new TransactionExportRequest
        {
            From = new DateTimeOffset(2026, 5, 1, 3, 0, 0, TimeSpan.FromHours(3)),
            To = new DateTimeOffset(2026, 6, 16, 8, 50, 4, 820, TimeSpan.Zero),
            Statuses = [PaymentStatus.Canceled, PaymentStatus.Confirmed],
            PaymentMethods = [PaymentMethod.SbpQr, PaymentMethod.Card],
        };

        Uri file = await _host.Client.Payments.ExportTransactionsAsync(request, format, TestContext.Current.CancellationToken);

        Assert.Equal($"https://api.test/transaction/export/{segment}", _host.Handler.LastRequest.Uri.AbsoluteUri);
        using JsonDocument body = JsonDocument.Parse(_host.Handler.LastRequest.Body);
        Assert.Equal("2026-05-01T00:00:00.000Z", body.RootElement.GetProperty("from").GetString());
        Assert.Equal("2026-06-16T08:50:04.820Z", body.RootElement.GetProperty("to").GetString());
        Assert.Equal(["2", "11"], body.RootElement.GetProperty("paymentMethods").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal(["6", "7"], body.RootElement.GetProperty("statuses").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal("UTC", body.RootElement.GetProperty("timeZoneId").GetString());
        Assert.Equal("https://files.platega.io/export/transactions.csv", file.AbsoluteUri);
    }

    [Fact]
    public async Task ExportTransactions_MapsEveryStatusToSupportCode()
    {
        _host.Handler.RespondWithFixture("export.json");

        await _host.Client.Payments.ExportTransactionsAsync(
            new TransactionExportRequest
            {
                From = DateTimeOffset.UnixEpoch,
                To = DateTimeOffset.UnixEpoch.AddDays(1),
                Statuses = [PaymentStatus.Pending, PaymentStatus.Canceled, PaymentStatus.Confirmed, PaymentStatus.Chargebacked],
            },
            TransactionExportFormat.Json,
            TestContext.Current.CancellationToken);

        using JsonDocument body = JsonDocument.Parse(_host.Handler.LastRequest.Body);
        Assert.Equal(["1", "6", "7", "9"], body.RootElement.GetProperty("statuses").EnumerateArray().Select(item => item.GetString()!));
    }

    [Fact]
    public async Task ExportTransactions_RejectsUnknownStatus()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _host.Client.Payments.ExportTransactionsAsync(
                new TransactionExportRequest
                {
                    From = DateTimeOffset.UnixEpoch,
                    To = DateTimeOffset.UnixEpoch.AddDays(1),
                    Statuses = [PaymentStatus.Unknown],
                },
                TransactionExportFormat.Csv,
                TestContext.Current.CancellationToken));
        Assert.Empty(_host.Handler.Requests);
    }

    [Theory]
    [InlineData("1", PaymentStatus.Pending)]
    [InlineData("6", PaymentStatus.Canceled)]
    [InlineData("7", PaymentStatus.Confirmed)]
    [InlineData("9", PaymentStatus.Chargebacked)]
    [InlineData("42", PaymentStatus.Unknown)]
    public async Task NumericStatus_IsReadWithSupportCodes(string code, PaymentStatus expected)
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"status\":" + code + "}");

        PlategaTransaction transaction = await _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, transaction.Status);
    }

    [Fact]
    public async Task ApiError_IsReportedWithStatusAndBody()
    {
        _host.Handler.Respond(HttpStatusCode.BadRequest, "{\"message\":\"amount is too small\"}");

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Payments.CreatePaymentAsync(NewRequest(null), TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("POST v2/transaction/process", exception.Endpoint);
        Assert.Contains("amount is too small", exception.ResponseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(PlategaTestHost.Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_ExplainsCredentials()
    {
        _host.Handler.Respond(HttpStatusCode.Unauthorized, string.Empty);

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Contains("MerchantId and Secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlategaErrorBody_IsParsedIntoFields()
    {
        _host.Handler.Respond(HttpStatusCode.Unauthorized, Fixture.Read("error-401.json"));

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Balances.GetBalancesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("Auth:SIGN_1001", exception.ErrorCode);
        Assert.Equal(4002, exception.ErrorType);
        Assert.Equal("Merchant secret key is not correct.", exception.ErrorMessage);
        Assert.Equal("00000000000000000000000000000000", exception.TraceId);
    }

    [Fact]
    public async Task NotFoundErrorBody_ExposesFieldDetails()
    {
        _host.Handler.Respond(HttpStatusCode.NotFound, Fixture.Read("error-404.json"));

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal("Common:NF_0001", exception.ErrorCode);
        Assert.Equal(4004, exception.ErrorType);
        PlategaErrorDetail detail = Assert.Single(exception.ErrorDetails);
        Assert.Equal("Id", detail.Key);
        Assert.EndsWith("not exist", detail.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<html>bad gateway</html>")]
    [InlineData("[1,2]")]
    [InlineData("{\"code\":42,\"type\":\"x\"}")]
    public async Task NonStandardErrorBody_LeavesFieldsEmpty(string body)
    {
        _host.Handler.Respond(HttpStatusCode.BadGateway, body);

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Balances.GetBalancesAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception.ErrorCode);
        Assert.Null(exception.ErrorType);
        Assert.Equal(body, exception.ResponseBody);
    }

    [Fact]
    public async Task MalformedResponse_IsWrappedInApiException()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "<html>gateway error</html>");

        PlategaApiException exception = await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.IsType<JsonException>(exception.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task UnknownStatus_DoesNotBreakDeserialization()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"status\":\"REFUNDING\",\"paymentDetails\":{\"amount\":10,\"currency\":\"BYN\"}}");

        PlategaTransaction transaction = await _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentStatus.Unknown, transaction.Status);
        Assert.Equal(new Money(10m, "BYN"), transaction.Amount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"transactionId\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"status\":\"PENDING\"}")]
    public async Task CreatePayment_RejectsSuccessWithoutIdOrLink(string body)
    {
        _host.Handler.Respond(HttpStatusCode.OK, body);

        await Assert.ThrowsAsync<PlategaApiException>(
            () => _host.Client.Payments.CreatePaymentAsync(NewRequest(null), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"paymentDetails\":{\"amount\":1e100,\"currency\":\"RUB\"}}")]
    [InlineData("{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"paymentDetails\":{\"amount\":[1],\"currency\":{\"code\":643}}}")]
    public async Task MalformedAmount_IsReadAsMissing(string body)
    {
        _host.Handler.Respond(HttpStatusCode.OK, body);

        PlategaTransaction transaction = await _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(transaction.Amount);
    }

    [Fact]
    public async Task NullStatus_IsReadAsUnknown()
    {
        _host.Handler.Respond(HttpStatusCode.OK, "{\"id\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\"status\":null}");

        PlategaTransaction transaction = await _host.Client.Payments.GetTransactionAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(PaymentStatus.Unknown, transaction.Status);
    }

    [Theory]
    [InlineData(PaymentMethod.Subscription)]
    [InlineData(PaymentMethod.Unknown)]
    public async Task CreatePayment_RejectsNonPaymentMethods(PaymentMethod method)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _host.Client.Payments.CreatePaymentAsync(NewRequest(method), TestContext.Current.CancellationToken));

        Assert.Empty(_host.Handler.Requests);
    }

    [Fact]
    public async Task CreatePayment_RejectsNonPositiveAmount()
    {
        CreatePaymentRequest request = NewRequest(null) with { Amount = new Money(0m, "RUB") };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _host.Client.Payments.CreatePaymentAsync(request, TestContext.Current.CancellationToken));
    }

    private static CreatePaymentRequest NewRequest(PaymentMethod? method) => new CreatePaymentRequest
    {
        Amount = new Money(500m, "RUB"),
        Description = "Оплата заказа №1",
        ReturnUrl = new Uri("https://shop.test/ok"),
        FailedUrl = new Uri("https://shop.test/fail"),
        Method = method,
        OrderId = "order-1",
        Metadata = new PayerMetadata { UserId = "42", UserName = "@buyer" },
    };
}

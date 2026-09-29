using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Platega.FakeServer;

/// <summary>Emulates the documented Platega endpoints with the documented response shapes.</summary>
public static class ApiEndpoints
{
    public static void MapPlategaApi(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder api = app.MapGroup(string.Empty).AddEndpointFilter(RequireMerchantHeaders);

        api.MapPost("/v2/transaction/process", CreateV2Async);
        api.MapPost("/transaction/process", CreateWithMethodAsync);
        api.MapGet("/transaction/{id:guid}", GetTransaction);
        api.MapGet("/h2h/{id:guid}", GetH2H);
        api.MapPost("/transaction/export/{format}", ExportAsync);
        api.MapGet("/balance/all", GetBalances);
        api.MapGet("/transaction/{id:guid}/cancel-supported", GetCancelSupported);
        api.MapPost("/transaction/{id:guid}/cancel", Cancel);
        api.MapGet("/subscription/{id:guid}", GetSubscription);
        api.MapGet("/subscription", ListSubscriptions);
        api.MapPost("/subscription/{id:guid}/cancel", CancelSubscription);

        app.MapPost("/api/v1/payouts/card-rub", CreatePayoutAsync);
        app.MapGet("/api/v1/cards", GetCards);
        app.MapGet("/exports/{token}", DownloadExport);
    }

    private static async ValueTask<object?> RequireMerchantHeaders(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        FakeOptions options = context.HttpContext.RequestServices.GetRequiredService<IOptions<FakeOptions>>().Value;
        string? merchantId = context.HttpContext.Request.Headers["X-MerchantId"];
        string? secret = context.HttpContext.Request.Headers["X-Secret"];

        bool merchantOk = Guid.TryParse(merchantId, out Guid parsed) && parsed == options.MerchantId;
        bool secretOk = secret is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(options.Secret));

        return merchantOk && secretOk ? await next(context) : Results.Unauthorized();
    }

    private static async Task<IResult> CreateV2Async(HttpRequest request, FakeStore store, CancellationToken cancellationToken)
    {
        JsonObject? body = await WireJson.ReadObjectAsync(request, cancellationToken);
        if (!TryReadPayment(body, requireMethod: false, out PaymentInput input, out string? error))
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, error);
        }

        FakeTransaction transaction = store.AddTransaction(input.Amount, input.Currency, null, input.Description, input.ReturnUrl, input.FailedUrl, input.Payload, input.OrderId);
        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["transactionId"] = transaction.Id,
            ["status"] = transaction.Status,
            ["url"] = PayUrl(store, transaction.Id),
            ["expiresIn"] = FormatLeft(transaction, store.Now),
            ["rate"] = store.Options.UsdtRate,
        });
    }

    private static async Task<IResult> CreateWithMethodAsync(HttpRequest request, FakeStore store, CancellationToken cancellationToken)
    {
        JsonObject? body = await WireJson.ReadObjectAsync(request, cancellationToken);
        if (body?.GetInt("paymentMethod") == 6)
        {
            return CreateSubscription(body, store);
        }

        if (!TryReadPayment(body, requireMethod: true, out PaymentInput input, out string? error))
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, error);
        }

        FakeTransaction transaction = store.AddTransaction(input.Amount, input.Currency, input.Method, input.Description, input.ReturnUrl, input.FailedUrl, input.Payload, input.OrderId);
        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["paymentMethod"] = FakeStatuses.MethodName(transaction.Method),
            ["transactionId"] = transaction.Id,
            ["redirect"] = PayUrl(store, transaction.Id),
            ["return"] = transaction.ReturnUrl,
            ["paymentDetails"] = string.Create(CultureInfo.InvariantCulture, $"{transaction.Amount:0.##} {transaction.Currency}"),
            ["status"] = transaction.Status,
            ["expiresIn"] = FormatLeft(transaction, store.Now),
            ["merchantId"] = store.Options.MerchantId,
            ["usdtRate"] = store.Options.UsdtRate,
        });
    }

    private static IResult CreateSubscription(JsonObject body, FakeStore store)
    {
        JsonObject? details = body["paymentDetails"] as JsonObject;
        int? amount = details?.GetInt("amount");
        int? interval = details?.GetInt("interval");
        int? count = details?.GetInt("intervalCount");
        string? description = body.GetString("description");
        int maxCount = interval switch { 1 => 31, 2 => 4, 3 => 12, 4 => 3, _ => 0 };

        if (amount is not > 0 || maxCount == 0 || count is null || count < 1 || count > maxCount || string.IsNullOrWhiteSpace(description))
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, "Invalid subscription: amount, interval (1-4), intervalCount within limits and description are required.");
        }

        FakeSubscription subscription = store.AddSubscription(amount.Value, details!.GetString("currency") ?? "RUB", interval!.Value, count.Value, description);
        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["paymentMethod"] = "Subscription",
            ["transactionId"] = subscription.Id,
            ["redirect"] = new Uri(store.Options.PublicBaseUrl, $"pay/subscription/{subscription.Id}").AbsoluteUri,
            ["status"] = FakeStatuses.Pending,
            ["merchantId"] = store.Options.MerchantId,
        });
    }

    private static IResult GetTransaction(Guid id, FakeStore store)
    {
        FakeTransaction? transaction = store.FindTransaction(id);
        if (transaction is null)
        {
            return WireJson.Error(StatusCodes.Status404NotFound, "Transaction not found");
        }

        decimal commission = transaction.Status == FakeStatuses.Confirmed ? transaction.Amount * store.Options.CommissionRate : 0m;
        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["id"] = transaction.Id,
            ["status"] = transaction.Status,
            ["paymentDetails"] = new Dictionary<string, object?> { ["amount"] = transaction.Amount, ["currency"] = transaction.Currency },
            ["merchantName"] = "Platega Fake Merchant",
            ["mechantId"] = store.Options.MerchantId,
            ["comission"] = decimal.Round(commission, 2),
            ["paymentMethod"] = FakeStatuses.MethodName(transaction.Method),
            ["expiresIn"] = FormatLeft(transaction, store.Now),
            ["return"] = transaction.ReturnUrl,
            ["comissionUsdt"] = store.ToUsdt(commission),
            ["amountUsdt"] = store.ToUsdt(transaction.Amount),
            ["qr"] = PayUrl(store, transaction.Id),
            ["payformSuccessUrl"] = new Uri(store.Options.PublicBaseUrl, "success").AbsoluteUri,
            ["payload"] = transaction.Payload,
            ["comissionType"] = 1,
            ["externalId"] = transaction.OrderId,
            ["description"] = transaction.Description,
        });
    }

    private static IResult GetH2H(Guid id, FakeStore store)
    {
        FakeTransaction? transaction = store.FindTransaction(id);
        return transaction is null
            ? WireJson.Error(StatusCodes.Status400BadRequest, "Transaction not found")
            : WireJson.Ok(new Dictionary<string, object?>
            {
                ["amount"] = transaction.Amount,
                ["qr"] = string.Create(CultureInfo.InvariantCulture, $"https://qr.nspk.ru/FAKE{transaction.Id:N}?type=02&sum={transaction.Amount * 100:0}&cur=RUB"),
            });
    }

    private static async Task<IResult> ExportAsync(string format, HttpRequest request, FakeStore store, CancellationToken cancellationToken)
    {
        JsonObject? body = await WireJson.ReadObjectAsync(request, cancellationToken);
        if (body is null
            || !DateTimeOffset.TryParse(body.GetString("from"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset from)
            || !DateTimeOffset.TryParse(body.GetString("to"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset to))
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, "from and to are required");
        }

        HashSet<int> methods = (body["paymentMethods"] as JsonArray ?? [])
            .Select(item => int.TryParse(item?.ToString(), CultureInfo.InvariantCulture, out int method) ? method : -1)
            .ToHashSet();

        HashSet<string> statuses = (body["statuses"] as JsonArray ?? [])
            .Select(item => FakeStatuses.FromExportCode(item?.ToString()))
            .ToHashSet(StringComparer.Ordinal);

        List<FakeTransaction> rows = store.Transactions()
            .Where(item => item.CreatedAt >= from && item.CreatedAt <= to)
            .Where(item => methods.Count == 0 || (item.Method is { } method && methods.Contains(method)))
            .Where(item => statuses.Count == 0 || statuses.Contains(item.Status))
            .ToList();

        FakeExport export = format.ToLowerInvariant() switch
        {
            "json" => new FakeExport("application/json", "transactions.json", JsonSerializer.Serialize(rows, WireJson.Options)),
            "csv" or "excel" => new FakeExport("text/csv", $"transactions-{format.ToLowerInvariant()}.csv", ToCsv(rows)),
            _ => new FakeExport(string.Empty, string.Empty, string.Empty),
        };

        if (export.FileName.Length == 0)
        {
            return WireJson.Error(StatusCodes.Status404NotFound, "Unknown export format");
        }

        string token = store.AddExport(export);
        return WireJson.Ok(new Dictionary<string, object?> { ["url"] = new Uri(store.Options.PublicBaseUrl, $"exports/{token}").AbsoluteUri });
    }

    private static IResult DownloadExport(string token, FakeStore store)
    {
        FakeExport? export = store.FindExport(token);
        return export is null
            ? Results.NotFound()
            : Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(export.Content)).ToArray(), export.ContentType, export.FileName);
    }

    private static IResult GetBalances(FakeStore store) =>
        WireJson.Ok(store.Balances()
            .Select(balance => new Dictionary<string, object?>
            {
                ["amount"] = balance.Amount,
                ["currency"] = balance.Currency,
                ["frozenBalance"] = balance.Frozen,
            })
            .ToList());

    private static IResult GetCancelSupported(Guid id, FakeStore store)
    {
        FakeTransaction? transaction = store.FindTransaction(id);
        if (transaction is null)
        {
            return WireJson.Error(StatusCodes.Status404NotFound, "Transaction not found");
        }

        bool supported = transaction.Status == FakeStatuses.Confirmed && transaction.SubscriptionId is null;
        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["supported"] = supported,
            ["totalDeductUsdt"] = supported ? store.ToUsdt(transaction.Amount) : 0m,
            ["penaltyNativeAmount"] = null,
            ["penaltyNativeCurrency"] = null,
            ["penaltyUsdt"] = null,
            ["penaltyConversionRate"] = null,
            ["blockReason"] = supported ? null : $"Transaction status is {transaction.Status}",
        });
    }

    private static IResult Cancel(Guid id, FakeStore store, CallbackQueue callbacks)
    {
        FakeTransaction? transaction = store.FindTransaction(id);
        if (transaction is null)
        {
            return WireJson.Error(StatusCodes.Status404NotFound, "Transaction not found");
        }

        bool accepted = transaction.SubscriptionId is null && store.TryRefund(id, out _);
        if (accepted)
        {
            callbacks.EnqueuePayment(transaction);
        }

        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["transactionId"] = id,
            ["accepted"] = accepted,
            ["manualControlRequired"] = false,
            ["message"] = accepted ? "Возврат выполнен" : $"Отмена невозможна: статус {transaction.Status}",
        });
    }

    private static IResult GetSubscription(Guid id, FakeStore store)
    {
        FakeSubscription? subscription = store.FindSubscription(id);
        return subscription is null
            ? WireJson.Error(StatusCodes.Status404NotFound, "Subscription not found")
            : WireJson.Ok(new Dictionary<string, object?>
            {
                ["id"] = subscription.Id,
                ["status"] = subscription.Status,
                ["amount"] = subscription.Amount,
                ["currencyCode"] = subscription.Currency,
                ["intervalUnit"] = FakeStatuses.IntervalName(subscription.Interval),
                ["intervalCount"] = subscription.IntervalCount,
                ["startAt"] = subscription.StartAt,
                ["nextChargeAt"] = subscription.NextChargeAt,
                ["lastChargeAt"] = subscription.LastChargeAt,
                ["description"] = subscription.Description,
                ["createdAt"] = subscription.CreatedAt,
                ["customerEmail"] = subscription.CustomerEmail,
                ["chargeMetrics"] = new Dictionary<string, object?>
                {
                    ["chargesTotal"] = subscription.ChargesSuccess + subscription.ChargesFailed,
                    ["chargesSuccess"] = subscription.ChargesSuccess,
                    ["chargesFailed"] = subscription.ChargesFailed,
                    ["totalAmount"] = subscription.TotalAmount,
                    ["lastChargeAt"] = subscription.LastChargeAt,
                    ["nextChargeAt"] = subscription.NextChargeAt,
                },
            });
    }

    private static IResult ListSubscriptions(HttpRequest request, FakeStore store)
    {
        int page = int.TryParse(request.Query["page"], CultureInfo.InvariantCulture, out int parsedPage) && parsedPage > 0 ? parsedPage : 1;
        int size = int.TryParse(request.Query["size"], CultureInfo.InvariantCulture, out int parsedSize) && parsedSize > 0 ? parsedSize : 20;
        int? status = int.TryParse(request.Query["status"], CultureInfo.InvariantCulture, out int parsedStatus) ? parsedStatus : null;
        DateTimeOffset? from = DateTimeOffset.TryParse(request.Query["from"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedFrom) ? parsedFrom : null;
        DateTimeOffset? to = DateTimeOffset.TryParse(request.Query["to"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedTo) ? parsedTo : null;

        List<FakeSubscription> filtered = store.Subscriptions()
            .Where(item => status is null || FakeStatuses.SubscriptionCode(item.Status) == status)
            .Where(item => from is null || item.CreatedAt >= from)
            .Where(item => to is null || item.CreatedAt <= to)
            .ToList();

        return WireJson.Ok(new Dictionary<string, object?>
        {
            ["items"] = filtered
                .Skip((page - 1) * size)
                .Take(size)
                .Select(item => new Dictionary<string, object?>
                {
                    ["id"] = item.Id,
                    ["status"] = FakeStatuses.SubscriptionCode(item.Status),
                    ["amount"] = item.Amount,
                    ["currencyCode"] = item.Currency,
                    ["intervalUnit"] = item.Interval,
                    ["intervalCount"] = item.IntervalCount,
                    ["nextChargeAt"] = item.NextChargeAt,
                    ["lastChargeAt"] = item.LastChargeAt,
                    ["customerEmail"] = item.CustomerEmail,
                    ["description"] = item.Description,
                    ["chargesCount"] = item.ChargesSuccess + item.ChargesFailed,
                    ["createdAt"] = item.CreatedAt,
                })
                .ToList(),
            ["total"] = filtered.Count,
            ["page"] = page,
            ["size"] = size,
        });
    }

    private static IResult CancelSubscription(Guid id, FakeStore store, CallbackQueue callbacks)
    {
        (bool found, bool changed) = store.CancelSubscription(id);
        if (!found)
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, "Subscription not found");
        }

        if (changed)
        {
            callbacks.EnqueueSubscriptionStatus(store.FindSubscription(id)!, "SUBSCRIPTION_CANCELLED");
        }

        return WireJson.Ok(new Dictionary<string, object?> { ["subscriptionId"] = id, ["status"] = "cancelled" });
    }

    private static async Task<IResult> CreatePayoutAsync(HttpRequest request, FakeStore store, PayoutSignatureVerifier verifier, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        byte[] body = buffer.ToArray();

        string? idempotencyKey = request.Headers["Idempotency-Key"];
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, "Idempotency-Key header is required");
        }

        string? authError = verifier.Verify(request, idempotencyKey, body);
        if (authError is not null)
        {
            return WireJson.Error(StatusCodes.Status401Unauthorized, authError);
        }

        if (store.FindPayout(idempotencyKey) is { } existing)
        {
            return PayoutResponse(existing);
        }

        JsonObject? payload = JsonNode.Parse(body) as JsonObject;
        int? amount = payload?.GetInt("amountRub");
        string? cardId = payload?.GetString("cardId");
        string? cardNumber = payload?.GetString("cardNumber");

        if (amount is null or < 1000 or > 87500)
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, "amountRub must be between 1000 and 87500");
        }

        string? masked = cardId is not null
            ? store.FindCard(cardId) is { Status: "ACTIVE" } card ? $"**** {card.Last4}" : null
            : cardNumber is { Length: >= 13 } ? $"**** {cardNumber[^4..]}" : null;

        if (masked is null)
        {
            return WireJson.Error(StatusCodes.Status400BadRequest, "Valid cardId or cardNumber is required");
        }

        return PayoutResponse(store.AddPayout(idempotencyKey, masked, amount.Value));
    }

    private static IResult PayoutResponse(FakePayout payout) =>
        WireJson.Ok(new Dictionary<string, object?>
        {
            ["withdrawalRecordId"] = payout.Id,
            ["status"] = "CREATED",
            ["cardMasked"] = payout.CardMasked,
            ["amountUsdtDebited"] = payout.AmountUsdt,
        });

    private static IResult GetCards(HttpRequest request, FakeStore store, PayoutSignatureVerifier verifier)
    {
        string? authError = verifier.Verify(request, string.Empty, []);
        if (authError is not null)
        {
            return WireJson.Error(StatusCodes.Status401Unauthorized, authError);
        }

        bool onlyActive = !string.Equals(request.Query["onlyActive"], "false", StringComparison.OrdinalIgnoreCase);
        return WireJson.Ok(store.Cards(onlyActive)
            .Select(card => new Dictionary<string, object?>
            {
                ["cardId"] = card.CardId,
                ["masked"] = card.Masked,
                ["last4"] = card.Last4,
                ["brand"] = card.Brand,
                ["label"] = card.Label,
                ["status"] = card.Status,
            })
            .ToList());
    }

    private static bool TryReadPayment(JsonObject? body, bool requireMethod, out PaymentInput input, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error)
    {
        input = default;
        JsonObject? details = body?["paymentDetails"] as JsonObject;
        decimal? amount = details?.GetDecimal("amount");
        string? currency = details?.GetString("currency");
        int? method = body?.GetInt("paymentMethod");
        string? description = body?.GetString("description");
        string? returnUrl = body?.GetString("return");
        string? failedUrl = body?.GetString("failedUrl");

        if (body is null || amount is not > 0 || string.IsNullOrWhiteSpace(currency))
        {
            error = "paymentDetails.amount and paymentDetails.currency are required";
            return false;
        }

        if (body["id"] is not null)
        {
            error = "Do not pass id: it is generated by the system";
            return false;
        }

        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(returnUrl) || string.IsNullOrWhiteSpace(failedUrl))
        {
            error = "description, return and failedUrl are required";
            return false;
        }

        if (requireMethod && method is not (2 or 3 or 11 or 12 or 13 or 14))
        {
            error = "paymentMethod must be one of 2, 3, 11, 12, 13, 14";
            return false;
        }

        input = new PaymentInput(amount.Value, currency, method, description, returnUrl, failedUrl, body.GetString("payload"), body.GetString("orderId"));
        error = null;
        return true;
    }

    private static string PayUrl(FakeStore store, Guid id) => new Uri(store.Options.PublicBaseUrl, $"pay/{id}").AbsoluteUri;

    private static string FormatLeft(FakeTransaction transaction, DateTimeOffset now)
    {
        TimeSpan left = transaction.ExpiresAt - now;
        return (left > TimeSpan.Zero ? left : TimeSpan.Zero).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string ToCsv(IEnumerable<FakeTransaction> rows)
    {
        StringBuilder csv = new StringBuilder("id;createdAt;status;amount;currency;method;description\n");
        foreach (FakeTransaction row in rows)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{row.Id};{row.CreatedAt:O};{row.Status};{row.Amount};{row.Currency};{FakeStatuses.MethodName(row.Method)};\"{row.Description.Replace("\"", "\"\"", StringComparison.Ordinal)}\"\n");
        }

        return csv.ToString();
    }

    private readonly record struct PaymentInput(decimal Amount, string Currency, int? Method, string Description, string ReturnUrl, string FailedUrl, string? Payload, string? OrderId);
}

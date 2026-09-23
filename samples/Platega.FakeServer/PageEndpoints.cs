using System.Globalization;
using System.Net;
using System.Text;

namespace Platega.FakeServer;

/// <summary>
/// HTML pages that stand in for the Platega payment form and a control panel for scenarios
/// the real service drives by itself (scheduled subscription charges, link expiry).
/// </summary>
public static class PageEndpoints
{
    private static readonly (int Code, string Name)[] SelectableMethods =
    [
        (2, "СБП (QR-код)"),
        (11, "Банковская карта"),
        (14, "SberPay"),
        (13, "Криптовалюта"),
        (12, "Международная оплата"),
        (3, "ЕРИП"),
    ];

    public static void MapFakePages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", ControlPanel);
        app.MapGet("/pay/{id:guid}", PaymentPage);
        app.MapPost("/pay/{id:guid}", CompletePaymentAsync);
        app.MapGet("/pay/subscription/{id:guid}", BindingPage);
        app.MapPost("/pay/subscription/{id:guid}", CompleteBindingAsync);
        app.MapPost("/control/subscriptions/{id:guid}/charge", ChargeSubscription);
        app.MapPost("/control/transactions/{id:guid}/expire", ExpireTransaction);
        app.MapGet("/success", () => Page("Оплата прошла", "<p>Платёж подтверждён. Можно закрыть страницу.</p>"));
    }

    private static IResult PaymentPage(Guid id, FakeStore store)
    {
        FakeTransaction? transaction = store.FindTransaction(id);
        if (transaction is null)
        {
            return Page("Платёж не найден", "<p>Ссылка недействительна.</p>", StatusCodes.Status404NotFound);
        }

        if (transaction.Status != FakeStatuses.Pending)
        {
            return Page("Платёж завершён", $"<p>Статус: <b>{transaction.Status}</b>.</p>{BackLink(transaction)}");
        }

        StringBuilder methods = new StringBuilder();
        if (transaction.Method is null)
        {
            methods.Append("<fieldset><legend>Способ оплаты (выбирает плательщик)</legend>");
            foreach ((int code, string name) in SelectableMethods)
            {
                string isChecked = code == 2 ? " checked" : string.Empty;
                methods.Append(CultureInfo.InvariantCulture, $"<label><input type=\"radio\" name=\"method\" value=\"{code}\"{isChecked}> {name}</label>");
            }

            methods.Append("</fieldset>");
        }
        else
        {
            methods.Append(CultureInfo.InvariantCulture, $"<p>Способ оплаты: <b>{E(FakeStatuses.MethodName(transaction.Method))}</b></p>");
        }

        string body = $"""
            <p class="amount">{transaction.Amount.ToString("0.##", CultureInfo.InvariantCulture)} {E(transaction.Currency)}</p>
            <p>{E(transaction.Description)}</p>
            <p class="muted">Ссылка действует до {transaction.ExpiresAt.ToLocalTime():HH:mm:ss}</p>
            <form method="post">
              {methods}
              <div class="actions">
                <button name="result" value="confirm" class="primary">Оплатить</button>
                <button name="result" value="decline">Отклонить</button>
              </div>
            </form>
            <p class="muted">Это эмулятор Platega: деньги не списываются.</p>
            """;
        return Page("Оплата заказа", body);
    }

    private static async Task<IResult> CompletePaymentAsync(Guid id, HttpRequest request, FakeStore store, CallbackQueue callbacks)
    {
        IFormCollection form = await request.ReadFormAsync();
        bool confirm = string.Equals(form["result"], "confirm", StringComparison.Ordinal);
        int? chosenMethod = int.TryParse(form["method"], CultureInfo.InvariantCulture, out int method) ? method : null;

        FakeTransaction? transaction = store.FindTransaction(id);
        if (transaction is null)
        {
            return Results.NotFound();
        }

        if (store.TryComplete(id, confirm ? FakeStatuses.Confirmed : FakeStatuses.Canceled, chosenMethod))
        {
            callbacks.EnqueuePayment(transaction);
        }

        string? target = transaction.Status == FakeStatuses.Confirmed ? transaction.ReturnUrl : transaction.FailedUrl;
        return string.IsNullOrWhiteSpace(target) ? Results.Redirect($"/pay/{id}") : Results.Redirect(target);
    }

    private static IResult BindingPage(Guid id, FakeStore store)
    {
        FakeSubscription? subscription = store.FindSubscription(id);
        if (subscription is null)
        {
            return Page("Подписка не найдена", "<p>Ссылка недействительна.</p>", StatusCodes.Status404NotFound);
        }

        if (subscription.Status != FakeStatuses.SubscriptionPendingAgreement)
        {
            return Page("Подписка", $"<p>Статус подписки: <b>{E(subscription.Status)}</b>.</p>");
        }

        string body = $"""
            <p class="amount">{subscription.Amount} {E(subscription.Currency)} / {subscription.IntervalCount} × {FakeStatuses.IntervalName(subscription.Interval)}</p>
            <p>{E(subscription.Description)}</p>
            <form method="post">
              <label>Email для уведомлений <input type="email" name="email" value="payer@example.com" required></label>
              <div class="actions">
                <button name="result" value="confirm" class="primary">Подтвердить привязку СБП</button>
                <button name="result" value="decline" formnovalidate>Отказаться</button>
              </div>
            </form>
            <p class="muted">Эмулятор: подтверждение в банке не требуется, первое списание выполняется сразу.</p>
            """;
        return Page("Привязка СБП-подписки", body);
    }

    private static async Task<IResult> CompleteBindingAsync(Guid id, HttpRequest request, FakeStore store, CallbackQueue callbacks)
    {
        IFormCollection form = await request.ReadFormAsync();
        bool confirm = string.Equals(form["result"], "confirm", StringComparison.Ordinal);
        string? email = form["email"];

        if (confirm)
        {
            bool activated = store.TrySetSubscriptionStatus(id, FakeStatuses.SubscriptionPendingAgreement, FakeStatuses.SubscriptionActive, subscription =>
            {
                subscription.CustomerEmail = email;
                subscription.StartAt = store.Now;
            });

            if (activated)
            {
                FakeSubscription subscription = store.FindSubscription(id)!;
                FakeTransaction? firstCharge = store.Charge(id, success: true);
                callbacks.EnqueueSubscriptionStatus(subscription, "SUBSCRIPTION_ACTIVATED");
                if (firstCharge is not null)
                {
                    callbacks.EnqueueSubscriptionCharge(firstCharge, subscription);
                }
            }

            return Page("Подписка оформлена", "<p>Привязка подтверждена, подписка активна.</p>");
        }

        if (store.TrySetSubscriptionStatus(id, FakeStatuses.SubscriptionPendingAgreement, FakeStatuses.SubscriptionFailed))
        {
            callbacks.EnqueueSubscriptionStatus(store.FindSubscription(id)!, "SUBSCRIPTION_FAILED");
        }

        return Page("Подписка не оформлена", "<p>Привязка отклонена.</p>");
    }

    private static IResult ChargeSubscription(Guid id, bool success, FakeStore store, CallbackQueue callbacks)
    {
        FakeTransaction? charge = store.Charge(id, success);
        if (charge is not null)
        {
            FakeSubscription subscription = store.FindSubscription(id)!;
            callbacks.EnqueueSubscriptionCharge(charge, subscription);
            if (!success)
            {
                callbacks.EnqueueSubscriptionStatus(subscription, "SUBSCRIPTION_PAST_DUE");
            }
        }

        return Results.Redirect("/");
    }

    private static IResult ExpireTransaction(Guid id, FakeStore store, CallbackQueue callbacks)
    {
        if (store.TryComplete(id, FakeStatuses.Canceled))
        {
            callbacks.EnqueuePayment(store.FindTransaction(id)!);
        }

        return Results.Redirect("/");
    }

    private static IResult ControlPanel(FakeStore store)
    {
        StringBuilder html = new StringBuilder();
        html.Append(CultureInfo.InvariantCulture, $"<p class=\"muted\">Callback URL: {E(store.Options.CallbackUrl?.AbsoluteUri ?? "не задан")} · MerchantId: {store.Options.MerchantId}</p>");

        html.Append("<h2>Транзакции</h2><table><tr><th>Создана</th><th>Сумма</th><th>Метод</th><th>Статус</th><th>Описание</th><th></th></tr>");
        foreach (FakeTransaction transaction in store.Transactions())
        {
            string action = transaction.Status == FakeStatuses.Pending && transaction.SubscriptionId is null
                ? $"<a href=\"/pay/{transaction.Id}\">форма</a> <form method=\"post\" action=\"/control/transactions/{transaction.Id}/expire\"><button>истечь</button></form>"
                : string.Empty;
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{transaction.CreatedAt.ToLocalTime():dd.MM HH:mm:ss}</td><td>{transaction.Amount:0.##} {E(transaction.Currency)}</td><td>{FakeStatuses.MethodName(transaction.Method)}</td><td>{transaction.Status}</td><td>{E(transaction.Description)}</td><td>{action}</td></tr>");
        }

        html.Append("</table><h2>Подписки</h2><table><tr><th>Создана</th><th>Сумма</th><th>Период</th><th>Статус</th><th>Списания</th><th>Следующее</th><th></th></tr>");
        foreach (FakeSubscription subscription in store.Subscriptions())
        {
            string action = subscription.Status == FakeStatuses.SubscriptionActive
                ? $"<form method=\"post\" action=\"/control/subscriptions/{subscription.Id}/charge?success=true\"><button>списать</button></form> <form method=\"post\" action=\"/control/subscriptions/{subscription.Id}/charge?success=false\"><button>неуспешное списание</button></form>"
                : subscription.Status == FakeStatuses.SubscriptionPendingAgreement ? $"<a href=\"/pay/subscription/{subscription.Id}\">форма привязки</a>" : string.Empty;
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{subscription.CreatedAt.ToLocalTime():dd.MM HH:mm:ss}</td><td>{subscription.Amount} {E(subscription.Currency)}</td><td>{subscription.IntervalCount} × {FakeStatuses.IntervalName(subscription.Interval)}</td><td>{subscription.Status}</td><td>{subscription.ChargesSuccess} / {subscription.ChargesFailed}</td><td>{subscription.NextChargeAt?.ToLocalTime():dd.MM.yyyy HH:mm}</td><td>{action}</td></tr>");
        }

        html.Append("</table><h2>Выводы</h2><table><tr><th>Создан</th><th>Карта</th><th>RUB</th><th>USDT</th><th>Idempotency-Key</th></tr>");
        foreach (FakePayout payout in store.Payouts())
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{payout.CreatedAt.ToLocalTime():dd.MM HH:mm:ss}</td><td>{E(payout.CardMasked)}</td><td>{payout.AmountRub}</td><td>{payout.AmountUsdt}</td><td>{E(payout.IdempotencyKey)}</td></tr>");
        }

        html.Append("</table>");
        return Page("Platega FakeServer", html.ToString(), wide: true);
    }

    private static string BackLink(FakeTransaction transaction)
    {
        string? target = transaction.Status == FakeStatuses.Confirmed ? transaction.ReturnUrl : transaction.FailedUrl;
        return string.IsNullOrWhiteSpace(target) ? string.Empty : $"<p><a href=\"{E(target)}\">Вернуться в магазин</a></p>";
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static IResult Page(string title, string body, int statusCode = StatusCodes.Status200OK, bool wide = false)
    {
        string width = wide ? "1100px" : "460px";
        string html = $$"""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{E(title)}}</title>
              <style>
                body { font-family: system-ui, sans-serif; background: #0f1320; color: #e8eaf2; margin: 0; padding: 32px 16px; }
                main { max-width: {{width}}; margin: 0 auto; background: #171c2c; border: 1px solid #2a3150; border-radius: 12px; padding: 24px; }
                h1 { font-size: 1.4rem; margin-top: 0; } h2 { font-size: 1.1rem; margin-top: 28px; }
                .badge { display: inline-block; font-size: .75rem; background: #3b2f10; color: #f5c451; border-radius: 6px; padding: 2px 8px; margin-bottom: 12px; }
                .amount { font-size: 2rem; font-weight: 700; margin: 8px 0; }
                .muted { color: #8b93b0; font-size: .85rem; }
                fieldset { border: 1px solid #2a3150; border-radius: 8px; margin: 16px 0; } fieldset label { display: block; padding: 4px 0; }
                label input[type=email] { display: block; width: 100%; box-sizing: border-box; margin-top: 6px; padding: 8px; border-radius: 6px; border: 1px solid #2a3150; background: #0f1320; color: inherit; }
                .actions { display: flex; gap: 12px; margin-top: 16px; }
                button { padding: 10px 16px; border-radius: 8px; border: 1px solid #2a3150; background: #222a42; color: inherit; cursor: pointer; }
                button.primary { background: #5b7cfa; border-color: #5b7cfa; color: #fff; }
                table { width: 100%; border-collapse: collapse; font-size: .85rem; } th, td { text-align: left; padding: 6px; border-bottom: 1px solid #2a3150; }
                td form { display: inline; } td button { padding: 4px 8px; }
                a { color: #8fa6ff; }
              </style>
            </head>
            <body><main><span class="badge">Platega FakeServer · эмулятор</span><h1>{{E(title)}}</h1>{{body}}</main></body>
            </html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, statusCode);
    }
}

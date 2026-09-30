# Platega .NET client

Unofficial .NET client for the [Platega](https://platega.io) payment gateway. Not affiliated with or endorsed by Platega.

- `Platega.Client`: payments (universal or fixed payment form), status, H2H, exports, balances, refunds, recurring SBP subscriptions, Payout API with HMAC-SHA256 signing, callback authentication and parsing.
- `Platega.Client.AspNetCore`: `MapPlategaCallback` endpoint for ASP.NET Core.

Targets .NET 8 and .NET 10.

## Register the client

`AddPlatega` binds the `Platega` configuration section (`MerchantId`, `Secret`, `BaseAddress`, `PayoutSecret`, `Timeout`):

```csharp
builder.Services.AddPlatega(builder.Configuration.GetSection("Platega"));
```

## Create a payment

With `Method = null` the payer chooses the payment method on the Platega page:

```csharp
CreatedPayment payment = await platega.Payments.CreatePaymentAsync(new CreatePaymentRequest
{
    Amount = new Money(1290m, "RUB"),
    Description = "Order 42",
    ReturnUrl = new Uri("https://shop.example.com/orders/42/result"),
    FailedUrl = new Uri("https://shop.example.com/orders/42/result?failed=1"),
    Payload = "42",
}, cancellationToken);

// Redirect the payer to payment.PaymentUrl.
```

## Receive callbacks

```csharp
builder.Services.AddScoped<IPlategaCallbackHandler, MyCallbackHandler>();

app.MapPlategaCallback("/platega/callback");
```

The endpoint authenticates `X-MerchantId`/`X-Secret`, answers the reachability probe Platega sends when the callback URL is saved, and returns `500` when the handler throws so that Platega retries. Keep the handler idempotent and take the final status from `GetTransactionAsync`.

## More

Full documentation, the API behavior verified against the live service, a local API emulator and demo apps: <https://github.com/Platonenkov/Platega>.

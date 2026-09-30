# Platega .NET client

[![CI](https://github.com/Platonenkov/Platega/actions/workflows/ci.yml/badge.svg)](https://github.com/Platonenkov/Platega/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Platega.Client.svg)](https://www.nuget.org/packages/Platega.Client)

Unofficial .NET client for the [Platega](https://platega.io) payment gateway, with an ASP.NET Core callback endpoint, a local API emulator, and two Blazor demo apps.

> This project is not affiliated with or endorsed by Platega. Platega publishes official SDKs for PHP, Python and Node.js only.

## Packages

| Package | Purpose |
|---|---|
| [`Platega.Client`](https://www.nuget.org/packages/Platega.Client) | API client: payments (universal or fixed payment form), status, H2H, exports, balances, refunds, recurring SBP subscriptions, Payout API with HMAC-SHA256 signing, callback authentication and parsing |
| [`Platega.Client.AspNetCore`](https://www.nuget.org/packages/Platega.Client.AspNetCore) | `MapPlategaCallback` endpoint for ASP.NET Core minimal APIs |

Both packages target .NET 8 and .NET 10.

```bash
dotnet add package Platega.Client
dotnet add package Platega.Client.AspNetCore
```

## Quick start

Register the client. `AddPlatega` binds the `Platega` configuration section (`MerchantId`, `Secret`, `BaseAddress`, `PayoutSecret`, `Timeout`) and fails at startup when `MerchantId` or `Secret` is empty:

```csharp
builder.Services.AddPlatega(builder.Configuration.GetSection("Platega"));
```

Keep the API key out of `appsettings*.json`: use user secrets locally and environment variables (`Platega__Secret`) in production.

Create a payment and send the payer to the returned page. When `Method` is `null`, the payer chooses the payment method on the Platega page (universal payment form):

```csharp
CreatedPayment payment = await platega.Payments.CreatePaymentAsync(new CreatePaymentRequest
{
    Amount = new Money(1290m, "RUB"),
    Description = "Order 42",
    ReturnUrl = new Uri("https://shop.example.com/orders/42/result"),
    FailedUrl = new Uri("https://shop.example.com/orders/42/result?failed=1"),
    OrderId = "42",
    Payload = "42",
}, cancellationToken);

// Redirect the payer to payment.PaymentUrl.
```

Here `platega` is an injected `IPlategaClient`. Its `Refunds`, `Balances`, `Subscriptions` and `Payouts` properties expose the other API areas.

## Receiving callbacks

Implement `IPlategaCallbackHandler` and map the endpoint from `Platega.Client.AspNetCore`:

```csharp
builder.Services.AddScoped<IPlategaCallbackHandler, MyCallbackHandler>();

app.MapPlategaCallback("/platega/callback");
```

Then set `https://<your-host>/platega/callback` as the callback URL in the Platega merchant cabinet. The endpoint answers:

- `200` to a reachability probe (an empty body or `{}`), without calling the handler. Platega sends such a probe when you save the callback URL;
- `401` when the `X-MerchantId`/`X-Secret` headers do not match the configuration;
- `400` when the body is not a Platega callback;
- `500` when the handler throws, so that Platega retries the delivery;
- `200` after the handler succeeds.

Guidelines for the handler:

- **Make it idempotent.** Platega retries an undelivered callback up to three times.
- **Re-read the status.** Callbacks are authenticated only by a static secret, so take the final status from `GetTransactionAsync` before fulfilling an order.
- **Do not match amounts exactly.** For SBP the callback `amount` includes the fee paid on top of the order amount (for example, 5.40 for a 5.00 order).
- **Rate-limit the endpoint.** It is public and marked `AllowAnonymous`, because Platega cannot pass your application's authentication. For example, with a `platega` policy registered through `AddRateLimiter`:

  ```csharp
  app.MapPlategaCallback("/platega/callback").RequireRateLimiting("platega");
  ```

## Errors and retries

API errors raise `PlategaApiException` with `StatusCode`, `Endpoint` and `ResponseBody`, and the parsed Platega error fields `ErrorCode`, `ErrorType`, `ErrorMessage`, `ErrorDetails` and `TraceId`. Quote `TraceId` when contacting Platega support.

The client never retries automatically. Payment creation has no idempotency key, so a blind retry can create a duplicate payment. Payouts require an idempotency key owned by the caller: persist it before the call and reuse it when the outcome is unknown.

A success response without a transaction id, or with a payment link that is not an absolute `http`/`https` URL, is treated as an error.

## Try it locally

The repository includes a Platega emulator and two Blazor Server demo apps: a shop (payer side) and an admin panel (merchant side). The demo UI is in Russian. No Platega account is needed.

Prerequisites: .NET SDK 10.

1. Start the emulator. Its control panel opens at `http://localhost:5190`:

   ```bash
   dotnet run --project samples/Platega.FakeServer --launch-profile http
   ```

2. In a second terminal, start the admin panel. It receives callbacks from the emulator:

   ```bash
   dotnet run --project samples/Platega.Demo.Admin --launch-profile http
   ```

3. In a third terminal, start the shop:

   ```bash
   dotnet run --project samples/Platega.Demo.Shop --launch-profile http
   ```

4. Open `http://localhost:5101`, pick a product and select **Оплатить** (Pay). The payment page opens in a new tab and the shop shows the order status. On the emulator page select **Оплатить** or **Отклонить** (Decline); the order status updates automatically.
5. Open the admin panel at `http://localhost:5201`. The `Development` password is `admin`.

Both apps share the SQLite database `%LOCALAPPDATA%\PlategaDemo\demo.db`; delete the folder to start over. The emulator control panel triggers events that the real service performs by itself, such as link expiry and subscription charges.

To run the demo against the real API, set `Platega:MerchantId`, `Platega:Secret` and `Platega:BaseAddress` (`https://app.platega.io/`) with `dotnet user-secrets` for both apps, and `Admin:Password` for the admin panel. Callbacks cannot reach a local machine, because Platega only accepts a public HTTPS callback URL; the admin panel polls pending payments every 30 seconds instead.

## Deploying the demo

`deploy/` contains a Dockerfile, a `docker-compose.yml` and a Caddyfile. Caddy obtains Let's Encrypt certificates for two host names and proxies to the shop and the admin panel:

1. Point two DNS names, for example `shop.<domain>` and `admin.<domain>`, to a Linux server with Docker Compose and open ports 80 and 443.
2. Copy `deploy/.env.example` to `deploy/.env` and fill in the values. The file holds secrets and is ignored by git.
3. Run `docker compose up -d --build` in `deploy/`. On a server with 1 GB of RAM, build the images elsewhere and transfer them as described at the top of `docker-compose.yml`.
4. Check the endpoint: a callback without headers returns `401`.

   ```bash
   curl -i -X POST https://<ADMIN_DOMAIN>/platega/callback -H "Content-Type: application/json" -d "{\"id\":\"00000000-0000-0000-0000-000000000001\",\"status\":\"CONFIRMED\"}"
   ```

5. Save `https://<ADMIN_DOMAIN>/platega/callback` as the callback URL in the merchant cabinet. Platega captures the URL when a transaction is created, so only transactions created afterwards send callbacks there.

After editing `.env`, run `docker compose up -d` again: containers read it only when they are created.

## Tests

```bash
dotnet test tests/Platega.Client.Tests
```

- **Unit tests** check request formats and response parsing against the Platega documentation and real responses.
- **Payout signing** is checked against signatures produced by the Python sample from the Platega documentation.
- **Integration tests** run the client against `Platega.FakeServer` (on .NET 10).
- **Live tests** (`Category=Live`) call the real API and are skipped without credentials.

To run the live tests, set the variables in your shell without saving the key to files, then run `dotnet test tests/Platega.Client.Tests -- --filter-trait "Category=Live" --output Detailed`:

| Variable | Required | Purpose |
|---|---|---|
| `PLATEGA_MERCHANT_ID` | Yes | Merchant id |
| `PLATEGA_SECRET` | Yes | API key |
| `PLATEGA_BASE_ADDRESS` | No | API address, `https://app.platega.io/` by default |
| `PLATEGA_PAYOUT_SECRET` | No | Enables the Payout API signature check |
| `PLATEGA_LIVE_CREATE` | No | `1` allows creating a 100 RUB payment link and a subscription; no money moves unless someone pays |

Raw responses are also written to `live-responses.log` next to the test binaries.

## Documentation

- [Design and Platega API behavior](docs/design.md): what the client handles, what was verified against the live API, and open questions.
- [Changelog](CHANGELOG.md)
- [Security policy](SECURITY.md)

## License

[MIT](LICENSE)

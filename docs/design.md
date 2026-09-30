# Design and Platega API behavior

This page describes how the client maps the Platega API, which API behaviors it compensates for, what was verified against the live API, and what remains open. For setup and usage, see the [README](../README.md).

## API coverage

| Area | Method and path | Authentication | Client method |
|---|---|---|---|
| Payment, payer chooses the method | `POST /v2/transaction/process` | `X-MerchantId`, `X-Secret` | `Payments.CreatePaymentAsync` with `Method = null` |
| Payment with a fixed method | `POST /transaction/process` | Same | `Payments.CreatePaymentAsync` with `Method` |
| Transaction status | `GET /transaction/{id}` | Same | `Payments.GetTransactionAsync` |
| H2H QR (enabled on request) | `GET /h2h/{id}` | Same | `Payments.GetH2HPaymentDataAsync` |
| CSV, Excel or JSON export | `POST /transaction/export/{format}` | Same | `Payments.ExportTransactionsAsync` |
| Balances | `GET /balance/all` | Same | `Balances.GetBalancesAsync` |
| Refund availability | `GET /transaction/{id}/cancel-supported` | Same | `Refunds.GetCancelAvailabilityAsync` |
| Cancel and refund | `POST /transaction/{id}/cancel` | Same | `Refunds.CancelTransactionAsync` |
| Create an SBP subscription | `POST /transaction/process` with `paymentMethod: 6` | Same | `Subscriptions.CreateAsync` |
| Get, list and cancel subscriptions | `GET /subscription/{id}`, `GET /subscription`, `POST /subscription/{id}/cancel` | Same | `Subscriptions.GetAsync`, `ListAsync`, `CancelAsync` |
| Payout to a RUB card (enabled on request) | `POST /api/v1/payouts/card-rub` | `Authorization: PG-HMAC` | `Payouts.CreateCardPayoutAsync` |
| Saved payout cards | `GET /api/v1/cards` | `Authorization: PG-HMAC` | `Payouts.GetSavedCardsAsync` |
| Callbacks | `POST` to the URL set in the cabinet | `X-MerchantId`, `X-Secret` | `PlategaCallbackParser`, `MapPlategaCallback` |

Payment lifecycle: `PENDING` → `CONFIRMED`, `CANCELED` or `CHARGEBACKED`.

Subscription lifecycle: `PendingAgreement` → `Active` → `PastDue`, `Cancelled` or `Failed`. The payer has 30 minutes to confirm the binding.

## API quirks handled by the client

- **Field casing.** Payment callbacks use camelCase, subscription callbacks use PascalCase. Parsing is case-insensitive.
- **Subscription status and interval.** `GET /subscription/{id}` returns names (`"Active"`, `"Month"`), `GET /subscription` returns numbers. The converters accept both.
- **Payment method.** Requests and callbacks carry a number, responses carry a name (`SBPQR`). Responses keep the name as is.
- **`paymentDetails` in the creation response** is either an object or a string such as `"100 RUB"`.
- **Misspelled field.** `mechantId` in the status response maps to `MerchantId`.
- **Unknown or null values.** Unknown statuses read as `Unknown` instead of failing the whole response. Currencies stay strings, so BYN, EUR or USDT are never read as RUB.
- **Content type.** Refund endpoints may answer `text/plain` with a JSON body, so the body is parsed regardless of the header.
- **Redirects.** Automatic redirects are disabled: the default handler forwards custom headers on a redirect, which would send `X-Secret` to the host in `Location`.
- **Timeouts.** The whole response is read within `Timeout`, so a stalled body cannot block a call indefinitely.
- **Payout signing.** The body is serialized once, and the same bytes are signed and sent. The signed path is the absolute path actually sent.
- **Callback URL probe.** When the URL is saved in the cabinet, Platega sends `POST {}` and saves the URL only after a success answer. The endpoint answers such probes with `200` without calling the handler and reports them through `IPlategaCallbackHandler.OnProbeAsync`.
- **Status code pages.** The callback endpoint disables status code pages for its own responses, so host middleware cannot turn `401`/`500` into another status.

## Verified against the live API

Verified on 2026-09-29 with a real merchant account. Real responses are stored in the test fixtures with the `-live` suffix and in `error-*.json`.

**Payments**

- `POST /v2/transaction/process` matches the documentation, but `expiresIn` is 30 minutes (the documentation says 15) and `rate` is `0`.
- `GET /transaction/{id}` also returns `refundStatus`, `refundStatusMessage` and `createdAt`. Until a method is chosen, `paymentMethod` and `qr` are `null`. The `orderId` from the request is not echoed in `externalId`.
- An unpaid payment is moved to `CANCELED`, with a callback, in batches: about three hours after `expiresIn` elapsed in the observed case. Support confirmed the automatic cancellation.
- For SBP the payer pays an 8% fee on top of the order amount: a 5.00 RUB order was paid as 5.40, and the callback `amount` was `5.40`. The merchant balance was credited with 5.00.

**Refunds**

- `cancel-supported` for a paid transaction returned `supported: true` and a `totalDeductUsdt` equal to the credited amount at the current rate.
- `POST /transaction/{id}/cancel` answered 200; the status changed to `CHARGEBACKED` and the callback arrived 73 seconds later. Right after `cancel`, the API still reports `CONFIRMED`.
- Refunds are charged to the USDT balance even for RUB payments, so the USDT balance can go negative. Refunds are allowed when either balance covers them.

**Callbacks**

- `CONFIRMED`, `CANCELED` and `CHARGEBACKED` payment callbacks were delivered and authenticated. Bodies are indented camelCase JSON; `paymentMethod` can be `null`; amounts have 16 decimal places.
- The callback URL is captured per transaction when the transaction is created. Transactions created before the URL was saved show "N/A" and never call it. The cabinet's "resend webhook" button sends a callback to the current URL.

**Errors and balances**

- Error bodies have the form `{"code":"Auth:SIGN_1001","type":4002,"message":"…","data":[{"key":"Id","message":"…"}],"traceId":"…"}` for 400, 401 and 404. With a wrong key, the API answers 401 before checking whether a transaction exists.
- `GET /balance/all` omits `frozenBalance` for RUB.

**Answers from Platega support**

- Export status codes: `1` PENDING, `6` CANCELED, `7` CONFIRMED, `9` CHARGEBACKED. `TransactionExportRequest.Statuses` takes `PaymentStatus` values and sends these codes.
- There is no sandbox. A separate test cabinet is available on request, but callbacks cannot be configured there. Otherwise, test with small payments and refunds.

## Open questions

1. **Numeric subscription status codes** in `GET /subscription` and its `status` filter are assumed to follow the declaration order of the documented schema: `0` PendingAgreement, `1` Active, `2` PastDue, `3` Cancelled, `4` Failed. They could not be verified: SBP subscriptions were not enabled on the account (`400 Common:VAL_0001` with `paymentMethod: Subscription`). The live test `Subscription_NumericListStatusMatchesNamedStatus` checks this once subscriptions are enabled.
2. **Signed path for payouts** excludes the query string, for example for `GET /api/v1/cards?onlyActive=false`. The Payout API was not enabled on the account.
3. **H2H** was not enabled on the account.
4. **Refund balance rule.** Support stated that a refund requires a balance covering the full amount paid including the fee, while the observed `totalDeductUsdt` equals the credited amount.

`Platega.FakeServer` implements the documentation as this client reads it, so passing integration tests do not confirm these points.

## Demo apps

1. The shop creates an order in SQLite and a payment in Platega; `orderId` and `payload` carry the order id.
2. The payment page opens in a new tab; the order status page polls every 3 seconds.
3. The admin panel receives callbacks, logs them, and takes the final status from `GET /transaction/{id}` rather than from the callback body.
4. `PendingPaymentsPoller` re-checks pending payments and subscriptions awaiting binding every 30 seconds for a day: a safety net for lost callbacks and the only update path on a local machine.
5. Status updates use optimistic concurrency and never move a status backwards, for example a stale `CONFIRMED` never overwrites `CHARGEBACKED`.
6. If the creation response was lost, the order is found by the callback `payload`; a subscription missing locally is restored from the API.

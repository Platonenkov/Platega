# Platega для .NET

Внутренняя .NET-библиотека для платёжного шлюза [Platega](https://docs.platega.io/) и демо-стенд из двух Blazor-приложений: магазин (плательщик) и админка (мерчант). Официального .NET SDK у Platega нет: есть только PHP, Python и Node.js.

Устройство решения и список допущений, которые ещё не проверены на живом API, описаны в [docs/design.md](docs/design.md).

## Состав

| Проект | Назначение |
|---|---|
| `src/Platega.Client` | Клиент API: платежи, статусы, H2H, выгрузки, балансы, возвраты, СБП-подписки, Payout API с HMAC-подписью, разбор callback-ов |
| `src/Platega.Client.AspNetCore` | Endpoint приёма callback-ов для ASP.NET Core |
| `samples/Platega.FakeServer` | Локальный эмулятор Platega: API, платёжная страница, отправка callback-ов |
| `samples/Platega.Demo.Data` | Общие для демо сущности, SQLite и сервисы |
| `samples/Platega.Demo.Shop` | Магазин: каталог, оплата, статус заказа, подписки |
| `samples/Platega.Demo.Admin` | Админка: балансы, транзакции и возвраты, подписки, выгрузки, выводы, журнал callback-ов |
| `tests/Platega.Client.Tests` | Юнит-тесты и интеграционные тесты клиента против эмулятора |
| `deploy/` | Docker-образ, `docker-compose.yml` и Caddy для тестового сервера |

## Быстрый старт на эмуляторе

Для запуска ключ Platega не нужен: демо-приложения в окружении `Development` обращаются к `Platega.FakeServer` с тестовыми учётными данными из `appsettings.Development.json`.

Требования: .NET SDK 10.

1. Запустите эмулятор:

   ```bash
   dotnet run --project samples/Platega.FakeServer --launch-profile http
   ```

   Пульт эмулятора откроется по адресу `http://localhost:5190`.

2. Во втором терминале запустите админку. Она принимает callback-и от эмулятора:

   ```bash
   dotnet run --project samples/Platega.Demo.Admin --launch-profile http
   ```

3. В третьем терминале запустите магазин:

   ```bash
   dotnet run --project samples/Platega.Demo.Shop --launch-profile http
   ```

4. Откройте `http://localhost:5101`, выберите товар и нажмите «Оплатить». На странице эмулятора нажмите «Оплатить» или «Отклонить». Магазин вернёт вас на страницу заказа, и статус обновится.

5. Откройте админку `http://localhost:5201`. Пароль для окружения `Development` — `admin`.

Оба приложения пишут в одну базу `%LOCALAPPDATA%\PlategaDemo\demo.db`. Чтобы начать с чистого листа, удалите эту папку.

Сценарии, которые у реальной Platega происходят сами, в эмуляторе запускаются вручную с пульта `http://localhost:5190`:

- истечение ссылки на оплату;
- успешное или неуспешное списание по подписке.

## Подключение к реальной Platega

Реальные ключи никогда не записываются в `appsettings*.json`. Локально их задают через user-secrets, на сервере — через переменные окружения.

1. В личном кабинете откройте «Настройки» и проверьте поле Callback URL. Там должен быть ваш адрес или пустое значение.

   > **Предупреждение.** Platega отправляет callback с заголовком `X-Secret`. Если в поле остался чужой адрес, API-ключ уйдёт на этот адрес. Замените URL до того, как нажмёте «Сгенерировать».

2. Сгенерируйте API-ключ и сразу сохраните его: полный ключ показывается один раз.

3. Задайте ключи для админки. Для магазина выполните те же команды с `samples/Platega.Demo.Shop`, кроме `PayoutSecret` и `Admin:Password`:

   ```bash
   dotnet user-secrets set "Platega:MerchantId" "<merchant-id>" --project samples/Platega.Demo.Admin
   dotnet user-secrets set "Platega:Secret" "<api-key>" --project samples/Platega.Demo.Admin
   dotnet user-secrets set "Platega:BaseAddress" "https://app.platega.io/" --project samples/Platega.Demo.Admin
   dotnet user-secrets set "Admin:Password" "<пароль-админки>" --project samples/Platega.Demo.Admin
   ```

   Здесь `<merchant-id>` и `<api-key>` — значения из «Настройки → Интеграция и API». Секрет Payout API (`Platega:PayoutSecret`) задают только после того, как менеджер подключит выводы.

4. Запустите магазин и админку, как в быстром старте. Эмулятор запускать не нужно.

При локальном запуске callback-и до вашей машины не дойдут: Platega принимает только публичный HTTPS-адрес. Статусы подтянет фоновый опрос админки: каждые 30 секунд она проверяет платежи в статусе `PENDING` за последние сутки. Чтобы проверить приём callback-ов, разверните демо на тестовом сервере.

## Развёртывание на тестовом сервере

Процедура проверена частично: образы собираются, контейнеры запускаются, страницы и endpoint callback отвечают локально. Выпуск сертификата Caddy на реальном домене ещё не проверялся.

Требования:

- Linux-сервер с Docker Compose;
- открытые порты 80 и 443;
- два DNS-имени, например `shop.<домен>` и `admin.<домен>`, указывающие на сервер.

1. Скопируйте репозиторий на сервер и перейдите в `deploy/`.
2. Создайте файл `.env` из шаблона и заполните его:

   ```bash
   cp .env.example .env
   ```

   Файл `.env` содержит секреты и не попадает в git.

3. Соберите образы и запустите стенд:

   ```bash
   docker compose up -d --build
   ```

   Caddy сам получит сертификаты Let's Encrypt для `SHOP_DOMAIN` и `ADMIN_DOMAIN`.

4. В личном кабинете Platega укажите Callback URL `https://<ADMIN_DOMAIN>/platega/callback`.
5. Проверьте, что endpoint отвечает. Запрос без заголовков должен вернуть `401`:

   ```bash
   curl -i -X POST https://<ADMIN_DOMAIN>/platega/callback -H "Content-Type: application/json" -d "{}"
   ```

База SQLite и ключи Data Protection хранятся в томе `demo-data`.

## Использование библиотеки

Библиотека регистрируется одной строкой. `AddPlatega` читает секцию `Platega`: `MerchantId`, `Secret`, `BaseAddress`, `PayoutSecret`, `Timeout`. При пустых `MerchantId` или `Secret` приложение не стартует:

```csharp
builder.Services.AddPlatega(builder.Configuration.GetSection("Platega"));
```

Создание платежа. Если `Method` равен `null`, способ оплаты выбирает плательщик на странице Platega:

```csharp
CreatedPayment payment = await platega.Payments.CreatePaymentAsync(new CreatePaymentRequest
{
    Amount = new Money(1290m, "RUB"),
    Description = "Заказ 42",
    ReturnUrl = new Uri("https://shop.example.com/orders/42/result"),
    FailedUrl = new Uri("https://shop.example.com/orders/42/result?failed=1"),
    Method = PaymentMethod.SbpQr,
    OrderId = "42",
}, cancellationToken);

// Redirect the payer to payment.PaymentUrl.
```

Здесь `platega` — внедрённый `IPlategaClient`. Остальные области API доступны через его свойства `Refunds`, `Balances`, `Subscriptions` и `Payouts`.

Приём callback-ов. Реализуйте `IPlategaCallbackHandler` и подключите endpoint из `Platega.Client.AspNetCore`:

```csharp
builder.Services.AddScoped<IPlategaCallbackHandler, MyCallbackHandler>();
app.MapPlategaCallback("/platega/callback");
```

Endpoint возвращает:

- `401`, если заголовки `X-MerchantId`/`X-Secret` не совпали с настройками;
- `400`, если тело не является callback-ом;
- `500`, если обработчик упал, — тогда Platega повторит доставку;
- `200` после успешной обработки.

Обработчик обязан быть идемпотентным. Статус заказа надёжнее перепроверить через `GetTransactionAsync`, как это делает `CallbackProcessor` в демо.

Ошибки API приходят как `PlategaApiException` с полями `StatusCode`, `Endpoint` и `ResponseBody`. Создание платежа не повторяется автоматически: у эндпоинта нет ключа идемпотентности, и повтор может создать дубль. Для выводов ключ идемпотентности обязателен и принадлежит вызывающему коду: сохраните его до вызова и повторяйте с ним же, если результат неизвестен.

## Тесты

Запуск всех тестов:

```bash
dotnet test tests/Platega.Client.Tests
```

Что проверяют тесты:

- **Юнит-тесты** — формат запросов и разбор ответов на примерах из документации Platega.
- **Подпись Payout** — сверяется с эталоном, посчитанным Python-примером из документации.
- **Интеграционные тесты** — прогоняют клиент против `Platega.FakeServer`.
- **Живые тесты** (`Category=Live`) — обращаются к настоящему API и без учётных данных пропускаются.

### Живые тесты

Живые тесты проверяют клиент на реальном API и выводят сырые ответы. По этим ответам сверяются допущения из [docs/design.md](docs/design.md#непроверенные-допущения). Задайте переменные окружения в своём терминале, не сохраняя секрет в файлах:

| Переменная | Обязательная | Назначение |
|---|---|---|
| `PLATEGA_MERCHANT_ID` | да | ID мерчанта |
| `PLATEGA_SECRET` | да | API-ключ |
| `PLATEGA_BASE_ADDRESS` | нет | Адрес API, по умолчанию `https://app.platega.io/` |
| `PLATEGA_PAYOUT_SECRET` | нет | Включает проверку подписи Payout API на списке сохранённых карт |
| `PLATEGA_LIVE_CREATE` | нет | Значение `1` разрешает создать платёжную ссылку на 100 RUB. Деньги не списываются, пока по ссылке никто не заплатил |

Без `PLATEGA_LIVE_CREATE` тесты только читают данные. Запуск с выводом ответов:

```bash
dotnet test tests/Platega.Client.Tests -- --filter-trait "Category=Live" --output Detailed
```

# Устройство решения

Страница описывает, как библиотека и демо работают с API Platega, и перечисляет допущения, которые ещё не проверены на живом API. Инструкции по запуску собраны в [README](../README.md).

## Возможности Platega

| Область | Метод и путь | Аутентификация | Метод клиента |
|---|---|---|---|
| Платёж, способ выбирает плательщик | `POST /v2/transaction/process` | `X-MerchantId`, `X-Secret` | `Payments.CreatePaymentAsync` с `Method = null` |
| Платёж с заданным методом | `POST /transaction/process` | то же | `Payments.CreatePaymentAsync` с `Method` |
| Статус транзакции | `GET /transaction/{id}` | то же | `Payments.GetTransactionAsync` |
| QR для H2H (по подключению) | `GET /h2h/{id}` | то же | `Payments.GetH2HPaymentDataAsync` |
| Выгрузка CSV, Excel, JSON | `POST /transaction/export/{format}` | то же | `Payments.ExportTransactionsAsync` |
| Балансы | `GET /balance/all` | то же | `Balances.GetBalancesAsync` |
| Проверка возможности отмены | `GET /transaction/{id}/cancel-supported` | то же | `Refunds.GetCancelAvailabilityAsync` |
| Отмена с возвратом | `POST /transaction/{id}/cancel` | то же | `Refunds.CancelTransactionAsync` |
| Создание СБП-подписки | `POST /transaction/process`, `paymentMethod: 6` | то же | `Subscriptions.CreateAsync` |
| Подписка, список, отмена | `GET /subscription/{id}`, `GET /subscription`, `POST /subscription/{id}/cancel` | то же | `Subscriptions.GetAsync`, `ListAsync`, `CancelAsync` |
| Вывод на карту RUB (по подключению) | `POST /api/v1/payouts/card-rub` | `Authorization: PG-HMAC` | `Payouts.CreateCardPayoutAsync` |
| Сохранённые карты | `GET /api/v1/cards` | `Authorization: PG-HMAC` | `Payouts.GetSavedCardsAsync` |
| Callback | POST на адрес из ЛК | `X-MerchantId`, `X-Secret` | `PlategaCallbackParser`, `MapPlategaCallback` |

Жизненный цикл платежа: `PENDING` → `CONFIRMED`, `CANCELED` или `CHARGEBACKED`. Ссылка на оплату действует 15 минут.

Жизненный цикл подписки: `PendingAgreement` → `Active` → `PastDue`, `Cancelled` или `Failed`. На подтверждение привязки даётся 30 минут.

## Особенности API, учтённые в клиенте

- **Регистр полей в callback-ах.** Callback платежа приходит в camelCase, callback-и подписок — в PascalCase. Разбор регистронезависимый.
- **Статус и период подписки.** `GET /subscription/{id}` возвращает имя (`"Active"`, `"Month"`), `GET /subscription` — число. Конвертеры принимают оба вида.
- **Метод оплаты.** В запросе и callback-е он передаётся числом, в ответах — строкой (`SBPQR`). Ответы хранят строку как есть, в поле `PaymentMethodName` или `PaymentMethod`.
- **`paymentDetails` в ответе на создание.** Это либо объект, либо строка вида `"100 RUB"`.
- **Опечатка в API.** Поле `mechantId` в ответе статуса сопоставлено со свойством `MerchantId`.
- **Неизвестные значения.** Неизвестный статус читается как `Unknown` и не роняет разбор ответа.
- **Валюта.** Хранится строкой, чтобы BYN, EUR или USDT не превращались в RUB.
- **Content-Type.** Эндпоинты возвратов могут отвечать с `text/plain`, поэтому тело читается как JSON независимо от заголовка.
- **Повторы.** Автоматических повторов нет: у создания платежа нет ключа идемпотентности. Для выводов ключ идемпотентности передаётся явно и возвращается в `CardPayoutResult.IdempotencyKey`, чтобы безопасно повторить тот же вывод.
- **Подпись Payout.** Тело сериализуется один раз, и эти же байты подписываются и отправляются.

## Поток оплаты в демо

1. Магазин создаёт заказ в SQLite и платёж в Platega. `orderId` и `payload` — это идентификатор заказа.
2. Плательщик уходит на платёжную страницу, а затем возвращается на `/orders/{id}/result`. Эта страница опрашивает статус каждые 3 секунды.
3. Platega отправляет callback в админку. `CallbackProcessor` пишет его в журнал и берёт итоговый статус из `GET /transaction/{id}`, а не из тела callback-а: callback защищён только статическим секретом.
4. `PendingPaymentsPoller` в админке раз в 30 секунд перепроверяет свежие `PENDING`-платежи и подписки, ожидающие привязки. Это страховка от потерянных callback-ов и единственный путь обновления при локальном запуске.
5. Списания по подписке сохраняются по идентификатору транзакции-списания, поэтому повторная доставка callback-а не увеличивает счётчик.

Магазин и админка — отдельные процессы с общей базой SQLite в режиме WAL.

## Непроверенные допущения

Эти пункты взяты из неполной документации и должны быть сверены с живым API, когда будет выдан ключ:

1. **Числовые коды статуса подписки.** `0` — `PendingAgreement`, `1` — `Active`, `2` — `PastDue`, `3` — `Cancelled`, `4` — `Failed`, по порядку объявления в схеме. Используются в `GET /subscription` и в фильтре `status`.
2. **Коды статусов в выгрузке.** В примере документации — `"6"`, `"7"`, их соответствие не описано. Клиент передаёт коды как есть (`TransactionExportRequest.StatusCodes`).
3. **PATH в подписи Payout.** Считается без query-строки, в частности для `GET /api/v1/cards?onlyActive=false`.
4. **Формат тела ошибок 400.** Не описан. Тело целиком сохраняется в `PlategaApiException.ResponseBody`.
5. **`localhost` в `return` и `failedUrl`.** Неизвестно, принимает ли Platega такие адреса. Если нет, магазин тоже нужно запускать на публичном адресе.
6. **Тестовая оплата.** Неизвестно, есть ли на тестовом аккаунте симуляция оплаты или проходят реальные деньги.

Эмулятор `Platega.FakeServer` реализует документацию в том же прочтении, поэтому зелёные интеграционные тесты не подтверждают эти пункты.

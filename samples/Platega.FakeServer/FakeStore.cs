using Microsoft.Extensions.Options;

namespace Platega.FakeServer;

public sealed class FakeTransaction
{
    public required Guid Id { get; init; }

    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    public int? Method { get; set; }

    public required string Description { get; init; }

    public string? ReturnUrl { get; init; }

    public string? FailedUrl { get; init; }

    public string? Payload { get; init; }

    public string? OrderId { get; init; }

    public string Status { get; set; } = FakeStatuses.Pending;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public Guid? SubscriptionId { get; init; }
}

public sealed class FakeSubscription
{
    public required Guid Id { get; init; }

    public required int Amount { get; init; }

    public required string Currency { get; init; }

    public required int Interval { get; init; }

    public required int IntervalCount { get; init; }

    public required string Description { get; init; }

    public string Status { get; set; } = FakeStatuses.SubscriptionPendingAgreement;

    public string? CustomerEmail { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? NextChargeAt { get; set; }

    public DateTimeOffset? LastChargeAt { get; set; }

    public int ChargesSuccess { get; set; }

    public int ChargesFailed { get; set; }

    public decimal TotalAmount { get; set; }
}

public sealed record FakePayout(Guid Id, string IdempotencyKey, string CardMasked, int AmountRub, decimal AmountUsdt, DateTimeOffset CreatedAt);

public sealed record FakeCard(string CardId, string Masked, string Last4, string Brand, string Label, string Status);

public sealed record FakeExport(string ContentType, string FileName, string Content);

public static class FakeStatuses
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Canceled = "CANCELED";
    public const string Chargebacked = "CHARGEBACKED";

    public const string SubscriptionPendingAgreement = "PendingAgreement";
    public const string SubscriptionActive = "Active";
    public const string SubscriptionPastDue = "PastDue";
    public const string SubscriptionCancelled = "Cancelled";
    public const string SubscriptionFailed = "Failed";

    /// <summary>Numeric codes used by <c>GET /subscription</c>; same assumption as the client library.</summary>
    public static int SubscriptionCode(string status) => status switch
    {
        SubscriptionPendingAgreement => 0,
        SubscriptionActive => 1,
        SubscriptionPastDue => 2,
        SubscriptionCancelled => 3,
        SubscriptionFailed => 4,
        _ => -1,
    };

    public static string IntervalName(int interval) => interval switch
    {
        1 => "Day",
        2 => "Week",
        3 => "Month",
        4 => "Year",
        _ => "Unknown",
    };

    public static string MethodName(int? method) => method switch
    {
        2 => "SBPQR",
        3 => "ERIP",
        6 => "Subscription",
        11 => "CARD",
        12 => "INTERNATIONAL",
        13 => "CRYPTO",
        14 => "SBERPAY",
        _ => "UNSELECTED",
    };
}

/// <summary>In-memory state of the emulator. All mutations happen under a single lock: this is a demo double, not a load target.</summary>
public sealed class FakeStore(IOptions<FakeOptions> options, TimeProvider timeProvider)
{
    private readonly Lock _sync = new Lock();
    private readonly Dictionary<Guid, FakeTransaction> _transactions = [];
    private readonly Dictionary<Guid, FakeSubscription> _subscriptions = [];
    private readonly Dictionary<string, FakePayout> _payoutsByKey = new Dictionary<string, FakePayout>(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeExport> _exports = new Dictionary<string, FakeExport>(StringComparer.Ordinal);
    private readonly List<FakeCard> _cards =
    [
        new FakeCard("a1b2c3d4-e5f6-7890-abcd-ef1234567890", "•••• •••• •••• 4242", "4242", "Mir", "Основная карта", "ACTIVE"),
        new FakeCard("b2c3d4e5-f6a7-8901-bcde-f12345678901", "•••• •••• •••• 1234", "1234", "Visa", "Запасная", "DISABLED"),
    ];

    private decimal _usdtSpent;

    public FakeOptions Options => options.Value;

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public FakeTransaction AddTransaction(decimal amount, string currency, int? method, string description, string? returnUrl, string? failedUrl, string? payload, string? orderId, Guid? subscriptionId = null)
    {
        FakeTransaction transaction = new FakeTransaction
        {
            Id = Guid.NewGuid(),
            Amount = amount,
            Currency = currency,
            Method = method,
            Description = description,
            ReturnUrl = returnUrl,
            FailedUrl = failedUrl,
            Payload = payload,
            OrderId = orderId,
            CreatedAt = Now,
            ExpiresAt = Now + Options.PaymentLifetime,
            SubscriptionId = subscriptionId,
        };

        lock (_sync)
        {
            _transactions[transaction.Id] = transaction;
        }

        return transaction;
    }

    public FakeTransaction? FindTransaction(Guid id)
    {
        lock (_sync)
        {
            return _transactions.GetValueOrDefault(id);
        }
    }

    public IReadOnlyList<FakeTransaction> Transactions()
    {
        lock (_sync)
        {
            return _transactions.Values.OrderByDescending(item => item.CreatedAt).ToList();
        }
    }

    /// <summary>Moves a pending transaction to a final status. Returns false when it was already final.</summary>
    public bool TryComplete(Guid id, string status, int? chosenMethod = null)
    {
        lock (_sync)
        {
            if (!_transactions.TryGetValue(id, out FakeTransaction? transaction) || transaction.Status != FakeStatuses.Pending)
            {
                return false;
            }

            transaction.Status = status;
            transaction.Method ??= chosenMethod;
            return true;
        }
    }

    public bool TryRefund(Guid id, out decimal deductedUsdt)
    {
        lock (_sync)
        {
            deductedUsdt = 0;
            if (!_transactions.TryGetValue(id, out FakeTransaction? transaction) || transaction.Status != FakeStatuses.Confirmed)
            {
                return false;
            }

            deductedUsdt = ToUsdt(transaction.Amount);
            transaction.Status = FakeStatuses.Chargebacked;
            return true;
        }
    }

    public IReadOnlyList<FakeTransaction> ExpireOverdue()
    {
        lock (_sync)
        {
            List<FakeTransaction> expired = _transactions.Values
                .Where(item => item.Status == FakeStatuses.Pending && item.SubscriptionId is null && item.ExpiresAt <= Now)
                .ToList();
            foreach (FakeTransaction transaction in expired)
            {
                transaction.Status = FakeStatuses.Canceled;
            }

            return expired;
        }
    }

    public FakeSubscription AddSubscription(int amount, string currency, int interval, int intervalCount, string description)
    {
        FakeSubscription subscription = new FakeSubscription
        {
            Id = Guid.NewGuid(),
            Amount = amount,
            Currency = currency,
            Interval = interval,
            IntervalCount = intervalCount,
            Description = description,
            CreatedAt = Now,
        };

        lock (_sync)
        {
            _subscriptions[subscription.Id] = subscription;
        }

        return subscription;
    }

    public FakeSubscription? FindSubscription(Guid id)
    {
        lock (_sync)
        {
            return _subscriptions.GetValueOrDefault(id);
        }
    }

    public IReadOnlyList<FakeSubscription> Subscriptions()
    {
        lock (_sync)
        {
            return _subscriptions.Values.OrderByDescending(item => item.CreatedAt).ToList();
        }
    }

    public bool TrySetSubscriptionStatus(Guid id, string expectedCurrent, string next, Action<FakeSubscription>? update = null)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(id, out FakeSubscription? subscription) || subscription.Status != expectedCurrent)
            {
                return false;
            }

            subscription.Status = next;
            update?.Invoke(subscription);
            return true;
        }
    }

    /// <summary>Cancels an active or past-due subscription. Idempotent for already cancelled ones.</summary>
    public (bool Found, bool Changed) CancelSubscription(Guid id)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(id, out FakeSubscription? subscription))
            {
                return (false, false);
            }

            if (subscription.Status is FakeStatuses.SubscriptionActive or FakeStatuses.SubscriptionPastDue or FakeStatuses.SubscriptionPendingAgreement)
            {
                subscription.Status = FakeStatuses.SubscriptionCancelled;
                subscription.NextChargeAt = null;
                return (true, true);
            }

            return (true, false);
        }
    }

    /// <summary>Simulates one scheduled charge. Returns the charge transaction or null when the subscription is not active.</summary>
    public FakeTransaction? Charge(Guid subscriptionId, bool success)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(subscriptionId, out FakeSubscription? subscription)
                || subscription.Status != FakeStatuses.SubscriptionActive)
            {
                return null;
            }

            FakeTransaction charge = new FakeTransaction
            {
                Id = Guid.NewGuid(),
                Amount = subscription.Amount,
                Currency = subscription.Currency,
                Method = 6,
                Description = subscription.Description,
                Status = success ? FakeStatuses.Confirmed : FakeStatuses.Canceled,
                CreatedAt = Now,
                ExpiresAt = Now,
                SubscriptionId = subscription.Id,
            };
            _transactions[charge.Id] = charge;

            subscription.LastChargeAt = Now;
            if (success)
            {
                subscription.ChargesSuccess++;
                subscription.TotalAmount += subscription.Amount;
                subscription.NextChargeAt = NextCharge(subscription, Now);
            }
            else
            {
                subscription.ChargesFailed++;
                subscription.NextChargeAt = null;
                subscription.Status = FakeStatuses.SubscriptionPastDue;
            }

            return charge;
        }
    }

    public IReadOnlyList<(string Currency, decimal Amount, decimal Frozen)> Balances()
    {
        lock (_sync)
        {
            decimal rub = _transactions.Values
                .Where(item => item.Status == FakeStatuses.Confirmed && item.Currency == "RUB")
                .Sum(item => item.Amount * (1 - Options.CommissionRate));
            decimal refundedUsdt = _transactions.Values
                .Where(item => item.Status == FakeStatuses.Chargebacked)
                .Sum(item => ToUsdt(item.Amount));
            decimal usdt = Options.InitialUsdtBalance - _usdtSpent - refundedUsdt;
            return [("RUB", decimal.Round(rub, 2), 0m), ("USDT", decimal.Round(usdt, 6), 0m)];
        }
    }

    public FakePayout? FindPayout(string idempotencyKey)
    {
        lock (_sync)
        {
            return _payoutsByKey.GetValueOrDefault(idempotencyKey);
        }
    }

    public FakePayout AddPayout(string idempotencyKey, string cardMasked, int amountRub)
    {
        lock (_sync)
        {
            if (_payoutsByKey.TryGetValue(idempotencyKey, out FakePayout? existing))
            {
                return existing;
            }

            decimal amountUsdt = decimal.Round(amountRub / Options.UsdtRate, 6);
            FakePayout payout = new FakePayout(Guid.NewGuid(), idempotencyKey, cardMasked, amountRub, amountUsdt, Now);
            _payoutsByKey[idempotencyKey] = payout;
            _usdtSpent += amountUsdt;
            return payout;
        }
    }

    public IReadOnlyList<FakePayout> Payouts()
    {
        lock (_sync)
        {
            return _payoutsByKey.Values.OrderByDescending(item => item.CreatedAt).ToList();
        }
    }

    public IReadOnlyList<FakeCard> Cards(bool onlyActive) =>
        onlyActive ? _cards.Where(card => card.Status == "ACTIVE").ToList() : _cards;

    public FakeCard? FindCard(string cardId) => _cards.FirstOrDefault(card => card.CardId == cardId);

    public string AddExport(FakeExport export)
    {
        string token = Guid.NewGuid().ToString("N");
        lock (_sync)
        {
            _exports[token] = export;
        }

        return token;
    }

    public FakeExport? FindExport(string token)
    {
        lock (_sync)
        {
            return _exports.GetValueOrDefault(token);
        }
    }

    public decimal ToUsdt(decimal amountRub) => decimal.Round(amountRub / Options.UsdtRate, 8);

    public static DateTimeOffset NextCharge(FakeSubscription subscription, DateTimeOffset from) => subscription.Interval switch
    {
        1 => from.AddDays(subscription.IntervalCount),
        2 => from.AddDays(7 * subscription.IntervalCount),
        3 => from.AddMonths(subscription.IntervalCount),
        4 => from.AddYears(subscription.IntervalCount),
        _ => from.AddMonths(1),
    };
}

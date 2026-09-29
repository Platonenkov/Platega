using System.Globalization;
using Platega.Subscriptions;

namespace Platega.Demo.Data;

/// <summary>Display helpers shared by both demo UIs.</summary>
public static class DemoFormat
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Money(decimal amount, string currency) => currency switch
    {
        "RUB" => $"{amount.ToString("N2", Russian)} ₽",
        "USDT" => Usdt(amount),
        _ => $"{amount.ToString("N2", Russian)} {currency}",
    };

    /// <summary>USDT amounts arrive with 16 decimals; up to 8 significant decimals are shown.</summary>
    public static string Usdt(decimal? amount) =>
        amount is { } value ? $"{value.ToString("#,0.########", Russian)} USDT" : "—";

    public static string Date(DateTimeOffset? value) =>
        value is { } date ? date.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", Russian) : "—";

    public static (string Text, string Css) Status(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => ("Ожидает оплаты", "text-bg-warning"),
        PaymentStatus.Confirmed => ("Оплачен", "text-bg-success"),
        PaymentStatus.Canceled => ("Отменён", "text-bg-secondary"),
        PaymentStatus.Chargebacked => ("Возврат", "text-bg-info"),
        _ => ("Ошибка", "text-bg-danger"),
    };

    public static (string Text, string Css) Status(SubscriptionStatus status) => status switch
    {
        SubscriptionStatus.PendingAgreement => ("Ждёт привязки", "text-bg-warning"),
        SubscriptionStatus.Active => ("Активна", "text-bg-success"),
        SubscriptionStatus.PastDue => ("Просрочена", "text-bg-danger"),
        SubscriptionStatus.Cancelled => ("Отменена", "text-bg-secondary"),
        SubscriptionStatus.Failed => ("Не оформлена", "text-bg-dark"),
        _ => ("Неизвестно", "text-bg-light"),
    };

    public static string MethodTitle(PaymentMethod? method) =>
        DemoCatalog.PaymentMethods.FirstOrDefault(option => option.Method == method)?.Title ?? method?.ToString() ?? "—";

    public static string Interval(SubscriptionInterval interval, int count)
    {
        string unit = interval switch
        {
            SubscriptionInterval.Day => "дн.",
            SubscriptionInterval.Week => "нед.",
            SubscriptionInterval.Month => "мес.",
            SubscriptionInterval.Year => "г.",
            _ => "?",
        };
        return count == 1 ? $"раз в 1 {unit}" : $"раз в {count} {unit}";
    }
}

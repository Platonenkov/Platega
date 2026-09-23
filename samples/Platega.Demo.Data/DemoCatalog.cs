using Platega.Subscriptions;

namespace Platega.Demo.Data;

public sealed record Product(string Code, string Name, string Description, decimal Price, string Currency = "RUB");

public sealed record SubscriptionPlan(string Code, string Name, string Description, int Amount, SubscriptionInterval Interval, int IntervalCount);

public sealed record PaymentMethodOption(PaymentMethod? Method, string Title, string Hint);

/// <summary>Static demo catalog shared by the shop and the admin panel.</summary>
public static class DemoCatalog
{
    public static IReadOnlyList<Product> Products { get; } =
    [
        new Product("coffee", "Кофе в зёрнах, 1 кг", "Эфиопия Иргачефф, свежая обжарка", 1290m),
        new Product("course", "Онлайн-курс по C#", "Асинхронность, производительность, тестирование", 4990m),
        new Product("ebook", "Электронная книга", "PDF и EPUB, доставка на email", 390m),
        new Product("stickers", "Набор стикеров", "10 виниловых стикеров для ноутбука", 150m),
    ];

    public static IReadOnlyList<SubscriptionPlan> Plans { get; } =
    [
        new SubscriptionPlan("weekly", "Неделя", "Доступ к материалам на неделю", 99, SubscriptionInterval.Week, 1),
        new SubscriptionPlan("basic", "Базовый", "Ежемесячная подписка", 199, SubscriptionInterval.Month, 1),
        new SubscriptionPlan("pro", "Про", "Ежемесячная подписка с поддержкой", 499, SubscriptionInterval.Month, 1),
    ];

    public static IReadOnlyList<PaymentMethodOption> PaymentMethods { get; } =
    [
        new PaymentMethodOption(null, "Выбрать на странице Platega", "Плательщик сам выбирает способ на платёжной форме"),
        new PaymentMethodOption(PaymentMethod.SbpQr, "СБП", "QR-код или ссылка в банковское приложение"),
        new PaymentMethodOption(PaymentMethod.Card, "Банковская карта", "Карточный эквайринг"),
        new PaymentMethodOption(PaymentMethod.SberPay, "SberPay", "Оплата через Сбер"),
        new PaymentMethodOption(PaymentMethod.Crypto, "Криптовалюта", "Оплата в USDT и других активах"),
        new PaymentMethodOption(PaymentMethod.International, "Международная оплата", "Карты иностранных банков"),
        new PaymentMethodOption(PaymentMethod.Erip, "ЕРИП", "Оплата из Беларуси"),
    ];

    public static Product? FindProduct(string code) =>
        Products.FirstOrDefault(product => string.Equals(product.Code, code, StringComparison.OrdinalIgnoreCase));

    public static SubscriptionPlan? FindPlan(string code) =>
        Plans.FirstOrDefault(plan => string.Equals(plan.Code, code, StringComparison.OrdinalIgnoreCase));
}

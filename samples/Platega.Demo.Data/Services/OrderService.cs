using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platega.Payments;

namespace Platega.Demo.Data.Services;

public sealed class ShopOptions
{
    public const string SectionName = "Shop";

    /// <summary>Public address of the shop; Platega redirects the payer back here.</summary>
    public Uri PublicBaseUrl { get; set; } = new Uri("http://localhost:5101/");
}

/// <summary>Creates orders and their Platega payments.</summary>
public sealed class OrderService(
    IDbContextFactory<DemoDbContext> dbFactory,
    IPlategaPaymentsClient payments,
    IOptions<ShopOptions> shopOptions,
    TimeProvider timeProvider)
{
    public async Task<Order> CreateAsync(string productCode, PaymentMethod? method, string customerName, CancellationToken cancellationToken)
    {
        Product product = DemoCatalog.FindProduct(productCode)
            ?? throw new ArgumentException($"Unknown product '{productCode}'.", nameof(productCode));
        string customer = string.IsNullOrWhiteSpace(customerName) ? "Гость" : customerName.Trim();
        DateTimeOffset now = timeProvider.GetUtcNow();

        Order order = new Order
        {
            Id = Guid.NewGuid(),
            ProductCode = product.Code,
            ProductName = product.Name,
            Amount = product.Price,
            Currency = product.Currency,
            Method = method,
            CustomerName = customer,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        Uri resultUrl = new Uri(shopOptions.Value.PublicBaseUrl, $"orders/{order.Id}/result");
        try
        {
            CreatedPayment payment = await payments.CreatePaymentAsync(
                new CreatePaymentRequest
                {
                    Amount = new Money(product.Price, product.Currency),
                    Description = $"{product.Name}, заказ {order.Id.ToString("N")[..8]}",
                    ReturnUrl = resultUrl,
                    FailedUrl = new Uri(resultUrl.AbsoluteUri + "?failed=1"),
                    Method = method,
                    OrderId = order.Id.ToString("D"),
                    Payload = order.Id.ToString("D"),
                    Metadata = new PayerMetadata { UserId = customer, UserName = customer },
                },
                cancellationToken);

            order.TransactionId = payment.TransactionId;
            order.PaymentUrl = payment.PaymentUrl;
            order.Status = payment.Status;
        }
        catch (Exception exception) when (exception is PlategaApiException or HttpRequestException or TaskCanceledException)
        {
            order.Status = PaymentStatus.Unknown;
            order.LastError = exception.Message;
            throw;
        }
        finally
        {
            order.UpdatedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return order;
    }

    public async Task<Order?> FindAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking().FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListAsync(int take, CancellationToken cancellationToken)
    {
        await using DemoDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Orders.AsNoTracking().OrderByDescending(order => order.CreatedAt).Take(take).ToListAsync(cancellationToken);
    }
}

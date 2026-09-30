using System.Globalization;
using System.Text;
using Platega.Http;
using Platega.Serialization;

namespace Platega.Subscriptions;

/// <summary>Recurring SBP subscriptions.</summary>
public interface IPlategaSubscriptionsClient
{
    /// <summary>Creates a subscription and returns the binding page the payer must be sent to.</summary>
    Task<CreatedSubscription> CreateAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns a subscription with its charge statistics.</summary>
    Task<PlategaSubscription> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Returns one page of subscriptions.</summary>
    Task<SubscriptionPage> ListAsync(SubscriptionListFilter? filter = null, CancellationToken cancellationToken = default);

    /// <summary>Stops future charges. Idempotent.</summary>
    Task<SubscriptionCancelResult> CancelAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}

internal sealed class PlategaSubscriptionsClient(PlategaConnection connection) : IPlategaSubscriptionsClient
{
    public async Task<CreatedSubscription> CreateAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        CreateSubscriptionWireRequest wire = new CreateSubscriptionWireRequest
        {
            PaymentMethod = (int)PaymentMethod.Subscription,
            PaymentDetails = new SubscriptionWireDetails
            {
                Amount = request.Amount,
                Currency = request.Currency,
                Interval = request.Interval,
                IntervalCount = request.IntervalCount,
            },
            Description = request.Description,
        };

        CreateSubscriptionWireResponse created = await connection
            .PostAsync(
                "transaction/process",
                wire,
                PlategaJsonContext.Relaxed.CreateSubscriptionWireRequest,
                PlategaJsonContext.Relaxed.CreateSubscriptionWireResponse,
                cancellationToken)
            .ConfigureAwait(false);

        Payments.PlategaPaymentsClient.EnsureCreated(created.TransactionId, created.Redirect, "POST transaction/process");
        return new CreatedSubscription
        {
            SubscriptionId = created.TransactionId,
            RedirectUrl = created.Redirect!,
            Status = created.Status,
            MerchantId = created.MerchantId,
        };
    }

    public Task<PlategaSubscription> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
        connection.GetAsync(
            $"subscription/{PlategaFormat.FormatId(subscriptionId)}",
            PlategaJsonContext.Relaxed.PlategaSubscription,
            cancellationToken);

    public Task<SubscriptionPage> ListAsync(SubscriptionListFilter? filter = null, CancellationToken cancellationToken = default)
    {
        SubscriptionListFilter effective = filter ?? new SubscriptionListFilter();
        if (effective.Page < 1 || effective.Size < 1)
        {
            throw new ArgumentException("Page and size must be positive.", nameof(filter));
        }

        return connection.GetAsync(BuildListPath(effective), PlategaJsonContext.Relaxed.SubscriptionPage, cancellationToken);
    }

    public Task<SubscriptionCancelResult> CancelAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
        connection.PostAsync(
            $"subscription/{PlategaFormat.FormatId(subscriptionId)}/cancel",
            PlategaJsonContext.Relaxed.SubscriptionCancelResult,
            cancellationToken);

    internal static string BuildListPath(SubscriptionListFilter filter)
    {
        StringBuilder path = new StringBuilder("subscription?page=")
            .Append(filter.Page.ToString(CultureInfo.InvariantCulture))
            .Append("&size=")
            .Append(filter.Size.ToString(CultureInfo.InvariantCulture));

        if (filter.Status is { } status)
        {
            if (!SubscriptionStatusConverter.Instance.TryGetCode(status, out int code))
            {
                throw new ArgumentException($"Status {status} cannot be used as a filter.", nameof(filter));
            }

            path.Append("&status=").Append(code.ToString(CultureInfo.InvariantCulture));
        }

        if (filter.From is { } from)
        {
            path.Append("&from=").Append(Uri.EscapeDataString(PlategaFormat.FormatTimestamp(from)));
        }

        if (filter.To is { } to)
        {
            path.Append("&to=").Append(Uri.EscapeDataString(PlategaFormat.FormatTimestamp(to)));
        }

        return path.ToString();
    }

    private static void Validate(CreateSubscriptionRequest request)
    {
        if (request.Amount <= 0)
        {
            throw new ArgumentException("Subscription amount must be positive.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("Subscription description is required.", nameof(request));
        }

        int maxCount = request.Interval switch
        {
            SubscriptionInterval.Day => 31,
            SubscriptionInterval.Week => 4,
            SubscriptionInterval.Month => 12,
            SubscriptionInterval.Year => 3,
            _ => throw new ArgumentException("Subscription interval is required.", nameof(request)),
        };

        if (request.IntervalCount < 1 || request.IntervalCount > maxCount)
        {
            throw new ArgumentException(
                $"Interval count for {request.Interval} must be between 1 and {maxCount}.",
                nameof(request));
        }
    }
}

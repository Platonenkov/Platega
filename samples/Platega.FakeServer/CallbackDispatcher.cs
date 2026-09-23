using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Platega.FakeServer;

/// <summary>Queue of callbacks to deliver, decoupled from the request that triggered them.</summary>
public sealed class CallbackQueue
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    public ChannelReader<string> Reader => _channel.Reader;

    /// <summary>Payment callback, camelCase as documented.</summary>
    public void EnqueuePayment(FakeTransaction transaction) =>
        Enqueue(new Dictionary<string, object?>
        {
            ["id"] = transaction.Id,
            ["amount"] = transaction.Amount,
            ["currency"] = transaction.Currency,
            ["status"] = transaction.Status,
            ["paymentMethod"] = transaction.Method ?? 0,
            ["payload"] = transaction.Payload ?? string.Empty,
        });

    /// <summary>Subscription charge callback, PascalCase as documented.</summary>
    public void EnqueueSubscriptionCharge(FakeTransaction charge, FakeSubscription subscription) =>
        Enqueue(new Dictionary<string, object?>
        {
            ["Id"] = charge.Id,
            ["Amount"] = charge.Amount,
            ["Currency"] = charge.Currency,
            ["Status"] = charge.Status,
            ["PaymentMethod"] = 6,
            ["Payload"] = string.Empty,
            ["SubscriptionId"] = subscription.Id,
            ["NextChargeAt"] = subscription.NextChargeAt,
        });

    /// <summary>Subscription status callback; <c>Id</c> equals the subscription id.</summary>
    public void EnqueueSubscriptionStatus(FakeSubscription subscription, string status) =>
        Enqueue(new Dictionary<string, object?>
        {
            ["Id"] = subscription.Id,
            ["Amount"] = subscription.Amount,
            ["Currency"] = subscription.Currency,
            ["Status"] = status,
            ["PaymentMethod"] = 6,
            ["Payload"] = string.Empty,
            ["SubscriptionId"] = subscription.Id,
            ["NextChargeAt"] = subscription.NextChargeAt,
        });

    private void Enqueue(Dictionary<string, object?> payload) =>
        _channel.Writer.TryWrite(JsonSerializer.Serialize(payload, WireJson.Options));
}

/// <summary>
/// Delivers callbacks with the <c>X-MerchantId</c>/<c>X-Secret</c> headers. Like Platega it retries up to 3 times,
/// but with a short delay so the demo does not wait 5 minutes.
/// </summary>
public sealed class CallbackDispatcher(
    CallbackQueue queue,
    IHttpClientFactory httpClientFactory,
    IOptions<FakeOptions> options,
    ILogger<CallbackDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "callbacks";
    private const int MaxAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (string body in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await DeliverAsync(body, stoppingToken);
        }
    }

    private async Task DeliverAsync(string body, CancellationToken cancellationToken)
    {
        Uri? target = options.Value.CallbackUrl;
        if (target is not { IsAbsoluteUri: true })
        {
            logger.LogInformation("Callback skipped (Fake:CallbackUrl is empty): {Body}", body);
            return;
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, target)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("X-MerchantId", options.Value.MerchantId.ToString("D"));
                request.Headers.Add("X-Secret", options.Value.Secret);

                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Callback delivered to {Target} (attempt {Attempt}): {Body}", target, attempt, body);
                    return;
                }

                logger.LogWarning("Callback to {Target} returned {Status} (attempt {Attempt})", target, (int)response.StatusCode, attempt);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Callback to {Target} failed (attempt {Attempt}): {Error}", target, attempt, exception.Message);
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        logger.LogError("Callback to {Target} dropped after {Attempts} attempts: {Body}", target, MaxAttempts, body);
    }
}

/// <summary>Cancels payments whose link expired, as Platega does, and notifies the merchant.</summary>
public sealed class ExpirySweeper(FakeStore store, CallbackQueue callbacks) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (FakeTransaction transaction in store.ExpireOverdue())
            {
                callbacks.EnqueuePayment(transaction);
            }
        }
    }
}

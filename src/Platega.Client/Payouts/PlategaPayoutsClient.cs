using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using Platega.Http;
using Platega.Serialization;

namespace Platega.Payouts;

/// <summary>
/// Payout API (enabled per merchant on request). Requests are signed with <see cref="PlategaOptions.PayoutSecret"/>.
/// </summary>
public interface IPlategaPayoutsClient
{
    /// <summary>True when <see cref="PlategaOptions.PayoutSecret"/> is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Creates a payout to a RUB card. The caller owns <paramref name="idempotencyKey"/>: persist it before the call
    /// and reuse it when the outcome is unknown (timeout, lost response), otherwise a retry may pay out twice.
    /// </summary>
    Task<CardPayoutResult> CreateCardPayoutAsync(
        CardPayoutRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>Returns saved payout cards; only active ones unless <paramref name="onlyActive"/> is false.</summary>
    Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(bool onlyActive = true, CancellationToken cancellationToken = default);
}

internal sealed class PlategaPayoutsClient(
    HttpClient httpClient,
    IOptionsMonitor<PlategaOptions> options,
    TimeProvider timeProvider) : IPlategaPayoutsClient
{
    internal const string PayoutPath = "/api/v1/payouts/card-rub";
    internal const string CardsPath = "/api/v1/cards";

    public bool IsConfigured => options.CurrentValue.PayoutsEnabled;

    public async Task<CardPayoutResult> CreateCardPayoutAsync(
        CardPayoutRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        Validate(request);

        string key = idempotencyKey;
        CardPayoutWireRequest wire = new CardPayoutWireRequest
        {
            CardId = string.IsNullOrWhiteSpace(request.CardId) ? null : request.CardId,
            CardNumber = string.IsNullOrWhiteSpace(request.CardNumber) ? null : request.CardNumber,
            AmountRub = request.AmountRub,
            PayoutMethod = "CARD",
            CurrencyRequested = "RUB",
        };

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(wire, PlategaJsonContext.Relaxed.CardPayoutWireRequest);
        using HttpRequestMessage message = CreateSignedRequest(HttpMethod.Post, PayoutPath, key, body);
        message.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        ByteArrayContent content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Content = content;

        CardPayoutResult result = await SendAsync(message, PayoutPath, PlategaJsonContext.Relaxed.CardPayoutResult, cancellationToken)
            .ConfigureAwait(false);
        if (result.WithdrawalRecordId == Guid.Empty)
        {
            throw new PlategaApiException(System.Net.HttpStatusCode.OK, $"POST {PayoutPath}", "Response has no withdrawalRecordId.");
        }

        return result with { IdempotencyKey = key };
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(bool onlyActive = true, CancellationToken cancellationToken = default)
    {
        string requestUri = onlyActive ? CardsPath : $"{CardsPath}?onlyActive=false";
        using HttpRequestMessage message = CreateSignedRequest(HttpMethod.Get, requestUri, string.Empty, []);
        return await SendAsync(message, CardsPath, PlategaJsonContext.Relaxed.ListSavedCard, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <remarks>
    /// The signed PATH is the absolute path actually sent (base address prefix included), without the query string.
    /// The documentation does not specify the query part; verify against the live API.
    /// </remarks>
    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string requestUri, string idempotencyKey, byte[] body)
    {
        PlategaOptions current = options.CurrentValue;
        if (!current.PayoutsEnabled)
        {
            throw new InvalidOperationException(
                "Payout API is not configured: set PlategaOptions.PayoutSecret (issued in the merchant cabinet, Payout API section).");
        }

        Uri target = new Uri(httpClient.BaseAddress!, requestUri.TrimStart('/'));
        long timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        string stringToSign = PlategaHmacSigner.BuildStringToSign(method.Method, target.AbsolutePath, timestamp, idempotencyKey, body);
        string signature = PlategaHmacSigner.Sign(current.PayoutSecret!, stringToSign);

        HttpRequestMessage message = new HttpRequestMessage(method, target);
        message.Headers.Authorization = new AuthenticationHeaderValue(
            PlategaHmacSigner.Scheme,
            PlategaHmacSigner.BuildAuthorizationParameter(current.MerchantId, timestamp, signature));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, string path, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient
            .SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        return await PlategaResponseReader
            .ReadAsync(response, $"{message.Method} {path}", typeInfo, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Validate(CardPayoutRequest request)
    {
        bool hasCardId = !string.IsNullOrWhiteSpace(request.CardId);
        bool hasCardNumber = !string.IsNullOrWhiteSpace(request.CardNumber);
        if (hasCardId == hasCardNumber)
        {
            throw new ArgumentException("Specify exactly one of CardId or CardNumber.", nameof(request));
        }

        if (hasCardNumber && (request.CardNumber!.Length is < 13 or > 19 || !request.CardNumber.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("Card number must contain 13 to 19 digits without separators.", nameof(request));
        }

        if (request.AmountRub is < CardPayoutRequest.MinAmountRub or > CardPayoutRequest.MaxAmountRub)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.AmountRub,
                $"Payout amount must be between {CardPayoutRequest.MinAmountRub} and {CardPayoutRequest.MaxAmountRub} RUB.");
        }
    }
}

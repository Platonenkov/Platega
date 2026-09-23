using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Platega.Serialization;
using Platega.Subscriptions;

namespace Platega.Callbacks;

/// <summary>
/// Authenticates and parses incoming Platega callbacks. Transport-agnostic: the ASP.NET Core endpoint
/// in <c>Platega.Client.AspNetCore</c> is a thin wrapper around it.
/// </summary>
public sealed class PlategaCallbackParser(IOptionsMonitor<PlategaOptions> options)
{
    /// <summary>Header carrying the merchant id.</summary>
    public const string MerchantIdHeader = "X-MerchantId";

    /// <summary>Header carrying the API key.</summary>
    public const string SecretHeader = "X-Secret";

    private const string SubscriptionStatusPrefix = "SUBSCRIPTION_";

    /// <summary>
    /// Checks the callback headers against the configured credentials using a constant-time comparison for the secret.
    /// </summary>
    public bool IsAuthentic(string? merchantIdHeader, string? secretHeader)
    {
        PlategaOptions current = options.CurrentValue;
        if (string.IsNullOrEmpty(secretHeader) || string.IsNullOrEmpty(current.Secret))
        {
            return false;
        }

        bool merchantMatches = Guid.TryParse(merchantIdHeader, out Guid merchantId) && merchantId == current.MerchantId;
        bool secretMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(secretHeader),
            Encoding.UTF8.GetBytes(current.Secret));

        return merchantMatches & secretMatches;
    }

    /// <summary>Parses a callback body. Returns false with an error description when the body is not a valid callback.</summary>
    public static bool TryParse(ReadOnlySpan<byte> body, [NotNullWhen(true)] out PlategaCallback? callback, [NotNullWhen(false)] out string? error)
    {
        callback = null;
        CallbackWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize(body, PlategaJsonContext.Relaxed.CallbackWire);
        }
        catch (JsonException exception)
        {
            error = $"Malformed JSON: {exception.Message}";
            return false;
        }

        if (wire is null)
        {
            error = "Empty callback body.";
            return false;
        }

        if (!Guid.TryParse(wire.Id, out Guid id))
        {
            error = "Callback id is missing or is not a GUID.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(wire.Status))
        {
            error = "Callback status is missing.";
            return false;
        }

        Guid? subscriptionId = Guid.TryParse(wire.SubscriptionId, out Guid parsedSubscriptionId) ? parsedSubscriptionId : null;
        string status = wire.Status.Trim();
        bool isSubscriptionStatus = status.StartsWith(SubscriptionStatusPrefix, StringComparison.OrdinalIgnoreCase);

        PlategaCallbackKind kind = isSubscriptionStatus
            ? PlategaCallbackKind.SubscriptionStatusChanged
            : subscriptionId is not null ? PlategaCallbackKind.SubscriptionCharge : PlategaCallbackKind.Payment;

        callback = new PlategaCallback
        {
            Kind = kind,
            Id = id,
            Amount = wire.Amount,
            Currency = wire.Currency,
            RawStatus = status,
            PaymentStatus = isSubscriptionStatus ? PaymentStatus.Unknown : PaymentStatusConverter.Instance.Parse(status),
            SubscriptionStatus = isSubscriptionStatus
                ? SubscriptionCallbackStatusConverter.Instance.Parse(status)
                : SubscriptionCallbackStatus.Unknown,
            PaymentMethod = wire.PaymentMethod is { } method ? (PaymentMethod)method : PaymentMethod.Unknown,
            Payload = wire.Payload,
            SubscriptionId = subscriptionId ?? (isSubscriptionStatus ? id : null),
            NextChargeAt = DateTimeOffset.TryParse(wire.NextChargeAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset nextChargeAt)
                ? nextChargeAt
                : null,
            RawJson = Encoding.UTF8.GetString(body),
        };
        error = null;
        return true;
    }
}

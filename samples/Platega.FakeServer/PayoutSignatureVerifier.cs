using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Platega.Http;

namespace Platega.FakeServer;

/// <summary>Validates <c>Authorization: PG-HMAC kid=..., ts=..., sig=...</c> the way the Payout API documents it.</summary>
public sealed class PayoutSignatureVerifier(IOptions<FakeOptions> options, TimeProvider timeProvider)
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromSeconds(300);

    public string? Verify(HttpRequest request, string idempotencyKey, byte[] body)
    {
        string? header = request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith(PlategaHmacSigner.Scheme + " ", StringComparison.Ordinal))
        {
            return "Missing PG-HMAC authorization.";
        }

        Dictionary<string, string> parts = header[(PlategaHmacSigner.Scheme.Length + 1)..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        if (!parts.TryGetValue("kid", out string? kid) || !Guid.TryParse(kid, out Guid merchantId) || merchantId != options.Value.MerchantId)
        {
            return "Unknown kid.";
        }

        if (!parts.TryGetValue("ts", out string? tsText) || !long.TryParse(tsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long timestamp))
        {
            return "Missing ts.";
        }

        DateTimeOffset signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if ((timeProvider.GetUtcNow() - signedAt).Duration() > AllowedClockSkew)
        {
            return "Timestamp outside the ±300 s window.";
        }

        if (!parts.TryGetValue("sig", out string? signature))
        {
            return "Missing sig.";
        }

        string stringToSign = PlategaHmacSigner.BuildStringToSign(request.Method, request.Path.Value!, timestamp, idempotencyKey, body);
        string expected = PlategaHmacSigner.Sign(options.Value.PayoutSecret, stringToSign);
        bool valid = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
        return valid ? null : "Signature mismatch.";
    }
}

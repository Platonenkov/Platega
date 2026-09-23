using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Platega.Http;

/// <summary>
/// Payout API request signature:
/// <c>Base64(HMAC-SHA256(secret, METHOD\nPATH\ntimestamp\nidempotencyKey\nsha256_hex(body)))</c>,
/// sent as <c>Authorization: PG-HMAC kid={merchantId}, ts={timestamp}, sig={signature}</c>.
/// The body must be sent with exactly the bytes that were signed.
/// </summary>
public static class PlategaHmacSigner
{
    /// <summary>Authorization scheme name.</summary>
    public const string Scheme = "PG-HMAC";

    /// <summary>Builds the string that is signed. GET requests pass an empty idempotency key and an empty body.</summary>
    public static string BuildStringToSign(string method, string path, long timestamp, string idempotencyKey, ReadOnlySpan<byte> body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return string.Join(
            '\n',
            method.ToUpperInvariant(),
            path,
            timestamp.ToString(CultureInfo.InvariantCulture),
            idempotencyKey ?? string.Empty,
            Sha256Hex(body));
    }

    /// <summary>Computes the Base64 HMAC-SHA256 signature of <paramref name="stringToSign"/>.</summary>
    public static string Sign(string secret, string stringToSign)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(stringToSign);

        byte[] key = Encoding.UTF8.GetBytes(secret);
        byte[] data = Encoding.UTF8.GetBytes(stringToSign);
        return Convert.ToBase64String(HMACSHA256.HashData(key, data));
    }

    /// <summary>Builds the <c>Authorization</c> header parameter (everything after the scheme name).</summary>
    public static string BuildAuthorizationParameter(Guid merchantId, long timestamp, string signature) =>
        string.Create(CultureInfo.InvariantCulture, $"kid={merchantId:D}, ts={timestamp}, sig={signature}");

    /// <summary>Lower-case hex SHA-256 of the body; for an empty body this is the well-known <c>e3b0c442...</c> constant.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> body) =>
        Convert.ToHexStringLower(SHA256.HashData(body));
}

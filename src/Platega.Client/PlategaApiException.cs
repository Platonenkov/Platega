using System.Net;

namespace Platega;

/// <summary>
/// Raised when the Platega API returns a non-success status code or a response that cannot be read.
/// </summary>
public sealed class PlategaApiException : Exception
{
    private const int MaxBodyLength = 2048;

    public PlategaApiException(HttpStatusCode statusCode, string endpoint, string? responseBody, Exception? innerException = null)
        : base(BuildMessage(statusCode, endpoint, responseBody), innerException)
    {
        StatusCode = statusCode;
        Endpoint = endpoint;
        ResponseBody = Truncate(responseBody);
    }

    /// <summary>HTTP status code returned by the API.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Method and path of the failed call, e.g. <c>GET transaction/{id}</c>.</summary>
    public string Endpoint { get; }

    /// <summary>Raw response body (truncated), useful for validation error details.</summary>
    public string? ResponseBody { get; }

    private static string BuildMessage(HttpStatusCode statusCode, string endpoint, string? responseBody)
    {
        string reason = statusCode switch
        {
            HttpStatusCode.BadRequest => "request validation failed",
            HttpStatusCode.Unauthorized => "authentication failed, check MerchantId and Secret",
            HttpStatusCode.Forbidden => "access denied, the feature may be disabled for this merchant",
            HttpStatusCode.NotFound => "resource not found",
            HttpStatusCode.OK => "response could not be parsed",
            _ => "unexpected response",
        };

        string details = string.IsNullOrWhiteSpace(responseBody) ? string.Empty : $": {Truncate(responseBody)}";
        return $"Platega API {endpoint} returned {(int)statusCode} ({reason}){details}";
    }

    private static string? Truncate(string? value) =>
        value is { Length: > MaxBodyLength } ? value[..MaxBodyLength] : value;
}

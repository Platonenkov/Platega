using System.Net;
using System.Text.Json;

namespace Platega;

/// <summary>
/// Raised when the Platega API returns a non-success status code or a response that cannot be read.
/// Platega error bodies look like <c>{"code":"Auth:SIGN_1001","type":4002,"message":"...","data":[],"traceId":"..."}</c>;
/// their fields are exposed as <see cref="ErrorCode"/>, <see cref="ErrorType"/>, <see cref="ErrorMessage"/> and <see cref="TraceId"/>.
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
        (ErrorCode, ErrorType, ErrorMessage, TraceId) = ParseErrorBody(responseBody);
    }

    /// <summary>HTTP status code returned by the API.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Method and path of the failed call, e.g. <c>GET transaction/{id}</c>.</summary>
    public string Endpoint { get; }

    /// <summary>Raw response body (truncated), useful for validation error details.</summary>
    public string? ResponseBody { get; }

    /// <summary>Platega error code, e.g. <c>Auth:SIGN_1001</c>.</summary>
    public string? ErrorCode { get; }

    /// <summary>Numeric Platega error type, e.g. <c>4002</c>.</summary>
    public int? ErrorType { get; }

    /// <summary>Human-readable error text from Platega.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Request trace id; quote it when contacting Platega support.</summary>
    public string? TraceId { get; }

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

    private static (string? Code, int? Type, string? Message, string? TraceId) ParseErrorBody(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody) || responseBody.TrimStart()[0] != '{')
        {
            return default;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            return (
                ReadString(root, "code"),
                root.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out int value) ? value : null,
                ReadString(root, "message"),
                ReadString(root, "traceId"));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static string? Truncate(string? value) =>
        value is { Length: > MaxBodyLength } ? value[..MaxBodyLength] : value;
}

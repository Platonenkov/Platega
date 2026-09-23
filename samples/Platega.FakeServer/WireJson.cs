using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Platega.FakeServer;

/// <summary>
/// Serialization that keeps property names exactly as written, so responses mirror the documented casing
/// (camelCase for payments, PascalCase for subscription callbacks).
/// </summary>
public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
        DictionaryKeyPolicy = null,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IResult Ok(object value) => Results.Json(value, Options);

    public static IResult Error(int statusCode, string message) =>
        Results.Json(new Dictionary<string, object?> { ["message"] = message }, Options, statusCode: statusCode);

    public static async Task<JsonObject?> ReadObjectAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? GetString(this JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue(out string? text) ? text : node[name]?.ToString();

    public static decimal? GetDecimal(this JsonObject node, string name)
    {
        if (node[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out decimal number))
        {
            return number;
        }

        return value.TryGetValue(out string? text)
            && decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }

    public static int? GetInt(this JsonObject node, string name)
    {
        decimal? value = node.GetDecimal(name);
        return value is { } number && number == decimal.Truncate(number) ? (int)number : null;
    }
}

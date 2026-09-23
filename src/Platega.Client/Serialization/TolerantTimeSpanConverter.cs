using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platega.Serialization;

/// <summary>
/// Reads <c>expiresIn</c> values such as <c>"00:15:00"</c>. An unexpected format yields <c>null</c>
/// instead of failing the whole response: the value is informational.
/// </summary>
internal sealed class TolerantTimeSpanConverter : JsonConverter<TimeSpan?>
{
    public override bool HandleNull => true;

    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && TimeSpan.TryParse(reader.GetString(), CultureInfo.InvariantCulture, out TimeSpan value))
        {
            return value;
        }

        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString("c", CultureInfo.InvariantCulture));
    }
}

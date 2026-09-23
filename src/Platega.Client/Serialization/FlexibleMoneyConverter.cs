using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platega.Serialization;

/// <summary>
/// <c>paymentDetails</c> in the create-transaction response is declared as <c>oneOf</c>:
/// either an object <c>{ amount, currency }</c> or a string such as <c>"100 RUB"</c>.
/// </summary>
internal sealed class FlexibleMoneyConverter : JsonConverter<Money?>
{
    public override bool HandleNull => true;

    public override Money? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return ParseText(reader.GetString());

            case JsonTokenType.StartObject:
                decimal? amount = null;
                string? currency = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    string? property = reader.GetString();
                    reader.Read();
                    if (string.Equals(property, "amount", StringComparison.OrdinalIgnoreCase))
                    {
                        amount = ReadDecimal(ref reader);
                    }
                    else if (string.Equals(property, "currency", StringComparison.OrdinalIgnoreCase)
                        && reader.TokenType == JsonTokenType.String)
                    {
                        currency = reader.GetString();
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return amount is null ? null : new Money(amount.Value, currency ?? string.Empty);

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, Money? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }

    internal static Money? ParseText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0
            || !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
        {
            return null;
        }

        return new Money(amount, parts.Length > 1 ? parts[1] : string.Empty);
    }

    /// <summary>Reads a decimal and always leaves the reader on the last token of the value, even for nested containers.</summary>
    private static decimal? ReadDecimal(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDecimal(out decimal number) ? number : null;
            case JsonTokenType.String:
                return decimal.TryParse(reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null;
            default:
                reader.Skip();
                return null;
        }
    }
}

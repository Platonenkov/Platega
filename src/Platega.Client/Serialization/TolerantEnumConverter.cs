using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platega.Serialization;

/// <summary>
/// Reads an enum from either its string name or its numeric code and never fails on unknown values:
/// the Platega API returns the same field as a string in one endpoint and as a number in another,
/// and new values must not break deserialization of the whole response.
/// </summary>
internal abstract class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly FrozenDictionary<string, TEnum> _byName;
    private readonly FrozenDictionary<int, TEnum> _byCode;
    private readonly FrozenDictionary<TEnum, string> _nameByValue;
    private readonly FrozenDictionary<TEnum, int> _codeByValue;
    private readonly TEnum _unknown;
    private readonly bool _writeAsCode;

    protected TolerantEnumConverter(
        IReadOnlyList<(string Name, TEnum Value)> names,
        IReadOnlyList<(int Code, TEnum Value)> codes,
        TEnum unknown,
        bool writeAsCode)
    {
        _byName = names.ToFrozenDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase);
        _byCode = codes.ToFrozenDictionary(item => item.Code, item => item.Value);
        _nameByValue = names.DistinctBy(item => item.Value).ToFrozenDictionary(item => item.Value, item => item.Name);
        _codeByValue = codes.DistinctBy(item => item.Value).ToFrozenDictionary(item => item.Value, item => item.Code);
        _unknown = unknown;
        _writeAsCode = writeAsCode;
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return Parse(reader.GetString());

            case JsonTokenType.Number:
                return reader.TryGetInt32(out int code) && _byCode.TryGetValue(code, out TEnum byCode) ? byCode : _unknown;

            default:
                reader.Skip();
                return _unknown;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (_writeAsCode && _codeByValue.TryGetValue(value, out int code))
        {
            writer.WriteNumberValue(code);
            return;
        }

        if (_nameByValue.TryGetValue(value, out string? name))
        {
            writer.WriteStringValue(name);
            return;
        }

        throw new JsonException($"Value '{value}' of {typeof(TEnum).Name} cannot be sent to the Platega API.");
    }

    public TEnum Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return _unknown;
        }

        string trimmed = text.Trim();
        if (_byName.TryGetValue(trimmed, out TEnum byName))
        {
            return byName;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code)
            && _byCode.TryGetValue(code, out TEnum byCode)
            ? byCode
            : _unknown;
    }

    public bool TryGetCode(TEnum value, out int code) => _codeByValue.TryGetValue(value, out code);

    public string? GetName(TEnum value) => _nameByValue.GetValueOrDefault(value);
}

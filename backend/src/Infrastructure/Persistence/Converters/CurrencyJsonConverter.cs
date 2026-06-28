using System.Text.Json;
using System.Text.Json.Serialization;
using JobOfferMatcher.Domain.Salary;

namespace JobOfferMatcher.Infrastructure.Persistence.Converters;

/// <summary>
/// Serializes <see cref="Currency"/> as its bare ISO code string (e.g. "PLN"). Needed because
/// Currency has only a private validated constructor — System.Text.Json cannot otherwise
/// round-trip it inside jsonb salary bands. Invalid stored codes fail loudly.
/// </summary>
public sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    public override Currency? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var code = reader.GetString();
        var result = Currency.Create(code);
        return result.IsSuccess
            ? result.Value
            : throw new JsonException($"Invalid currency code in stored data: '{code}'.");
    }

    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Code);
}

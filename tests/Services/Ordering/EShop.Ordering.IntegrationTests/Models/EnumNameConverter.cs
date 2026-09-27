using System.Text.Json;
using System.Text.Json.Serialization;

namespace EShop.Ordering.IntegrationTests.Models;

/// <summary>
/// Reads an enum only from its exact PascalCase name (frontend-contracts F-01). Stricter than the server's converter on
/// purpose: every test that reads a status through a response model then fails if the API goes back to integers or
/// changes the casing, instead of quietly accepting either.
/// </summary>
public sealed class EnumNameConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected {typeof(TEnum).Name} as a name string, got {reader.TokenType}.");

        var name = reader.GetString();
        if (!Enum.GetNames<TEnum>().Contains(name, StringComparer.Ordinal))
            throw new JsonException($"'{name}' is not a PascalCase {typeof(TEnum).Name} name.");

        return Enum.Parse<TEnum>(name!);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

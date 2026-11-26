using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HockeySeason.Auth;

/// <summary>
/// Reads an OIDC timestamp that the identity provider publishes either as a
/// JSON number of Unix seconds (Auth0's <c>/userinfo</c>) or as an RFC 3339
/// string (the same claim in a token). Deserializing into a plain
/// <see cref="string"/> throws on the numeric form — which made the whole
/// userinfo payload unreadable and surfaced as a 502 — so both shapes are
/// accepted and normalized to an ISO-8601 string for the SPA.
/// </summary>
public sealed class OidcTimestampConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var seconds) =>
                DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("o", CultureInfo.InvariantCulture),
            _ => null,
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(value);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlPlus.Api.Security;

/// <summary>
/// Auditoría ZAP del 27/11/2026 (ZAP-ACT-001): PostgreSQL no acepta el carácter nulo (U+0000) en un texto. Un inicio
/// de sesión con «\u0000» en el usuario llegaba a la base y respondía 500. Este convertidor rechaza el carácter en
/// cualquier texto del cuerpo JSON, y la API responde 400 antes de tocar la base.
/// </summary>
public sealed class RejectNullCharacterStringConverter : JsonConverter<string>
{
    public const string Message = "El texto no puede contener el carácter nulo.";

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is not null && value.Contains('\0', StringComparison.Ordinal)) throw new JsonException(Message);
        return value;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>El mismo rechazo para la ruta y los parámetros de consulta (…?buscar=%00).</summary>
public static class NullCharacterRequestGuard
{
    public static bool HasNullCharacter(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Path.Value ?? string.Empty).Contains('\0', StringComparison.Ordinal)) return true;
        foreach (var parameter in request.Query)
        {
            if (parameter.Key.Contains('\0', StringComparison.Ordinal)) return true;
            foreach (var value in parameter.Value)
                if (value is not null && value.Contains('\0', StringComparison.Ordinal)) return true;
        }
        return false;
    }
}

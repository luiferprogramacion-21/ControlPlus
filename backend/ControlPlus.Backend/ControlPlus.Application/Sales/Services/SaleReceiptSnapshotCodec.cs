using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Sales.Services;

// The version belongs to the data schema. It is independent of document and template versions.
public static class SaleReceiptSnapshotCodec
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static (string Data, string Hash) Encode(SaleReceiptDto receipt,
        IReadOnlyCollection<SalePaymentDto> payments)
    {
        var snapshot = new ReceiptSnapshotV1(SchemaVersion, receipt,
            payments.Select(x => new PaymentMethodSnapshot(x.MethodCode, x.MethodName)).ToArray());
        var data = JsonSerializer.Serialize(snapshot, JsonOptions);
        return (data, Hash(Canonicalize(data)));
    }

    public static bool TryDecode(DocumentoEmitido document, Guid saleId, out SaleReceiptDto? receipt)
    {
        receipt = null;
        if (document.VersionDocumento != 1) return false;
        try
        {
            var canonical = Canonicalize(document.DatosSnapshot);
            if (!string.Equals(Hash(canonical), document.HashSnapshot, StringComparison.Ordinal)) return false;
            var snapshot = JsonSerializer.Deserialize<ReceiptSnapshotV1>(document.DatosSnapshot, JsonOptions);
            if (snapshot is null || snapshot.SchemaVersion != SchemaVersion ||
                snapshot.Receipt is null || snapshot.Receipt.Lines is null ||
                snapshot.Receipt.Payments is null || snapshot.PaymentMethods is null ||
                snapshot.Receipt.SaleId != saleId || snapshot.Receipt.State != "CONFIRMADA" ||
                snapshot.Receipt.VisibleNumber != document.NumeroVisible ||
                snapshot.PaymentMethods.Count != snapshot.Receipt.Payments.Count ||
                !snapshot.PaymentMethods.Zip(snapshot.Receipt.Payments).All(pair =>
                    pair.First is not null && pair.Second is not null &&
                    pair.First.Code == pair.Second.MethodCode &&
                    !string.IsNullOrWhiteSpace(pair.First.Name)))
                return false;
            receipt = snapshot.Receipt;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException)
        {
            return false;
        }
    }

    // jsonb can reorder object properties and normalize numeric text. Canonicalization makes
    // the stored hash reproducible from the value returned by PostgreSQL.
    private static byte[] Canonicalize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var output = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(output))
            WriteCanonical(document.RootElement, writer);
        return output.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException("Unsupported receipt snapshot value.");
        }
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ReceiptSnapshotV1(int SchemaVersion, SaleReceiptDto Receipt,
        IReadOnlyCollection<PaymentMethodSnapshot> PaymentMethods);

    private sealed record PaymentMethodSnapshot(string Code, string Name);
}

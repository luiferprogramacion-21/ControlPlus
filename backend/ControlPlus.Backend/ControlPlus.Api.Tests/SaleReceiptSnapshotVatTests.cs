using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Sales.Services;
using ControlPlus.Domain.OfficialModel;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class SaleReceiptSnapshotVatTests
{
    [Fact]
    public void OldSnapshotWithoutVatFieldsKeepsItsHashAndReprintsWithoutVat()
    {
        var saleId = Guid.CreateVersion7();
        var receipt = new SaleReceiptDto(saleId, "V-1", DateTimeOffset.UtcNow,
            new DateOnly(2026, 11, 11), "CONTADO", "CONFIRMADA", 1000m, 0m, 1000m, 0m,
            [], [new SaleReceiptPaymentDto("EFECTIVO", 1000m, 1000m, 0m)]);
        var (newData, _) = SaleReceiptSnapshotCodec.Encode(receipt,
            [new SalePaymentDto(Guid.CreateVersion7(), "EFECTIVO", "Efectivo", 1000m, 1000m, 0m, null)]);
        var root = JsonNode.Parse(newData)!.AsObject();
        var oldReceipt = root["receipt"]!.AsObject();
        oldReceipt.Remove("vatPercentage");
        oldReceipt.Remove("taxableBaseTotal");
        oldReceipt.Remove("vatTotal");
        var oldData = root.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(root)))).ToLowerInvariant();
        var document = new DocumentoEmitido
        {
            VersionDocumento = 1, DatosSnapshot = oldData, HashSnapshot = hash,
            NumeroVisible = receipt.VisibleNumber
        };

        Assert.True(SaleReceiptSnapshotCodec.TryDecode(document, saleId, out var restored));
        Assert.Null(restored!.VatPercentage);
        Assert.Null(restored.TaxableBaseTotal);
        Assert.Null(restored.VatTotal);
        Assert.Equal(hash, document.HashSnapshot);
        var reprint = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(restored, "Baratísimo", 32, true));
        Assert.DoesNotContain("Base gravable", reprint);
        Assert.DoesNotContain("incluido", reprint);
        Assert.Contains("REIMPRESIÓN", reprint);
    }

    // A JSON null is a null node (for example the absent customer name), so it must be handled explicitly.
    private static string Canonical(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => JsonSerializer.Serialize(x.Key) + ":" + Canonical(x.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node.ToJsonString()
    };
}

using System.Text;
using ControlPlus.Application.Printing;
using ControlPlus.Application.Sales.Contracts;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class ReceiptTextRendererTests
{
    [Fact]
    public void Render_UsesReceiptValuesAndSanitizesControlCharacters()
    {
        var receipt = new SaleReceiptDto(
            Guid.CreateVersion7(), "V-000123\nINJECTADO", new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 11, 6), "CONTADO", "CONFIRMADA", 20000m, 2000m, 18000m, 2000m,
            [new SaleLineDto(Guid.CreateVersion7(), Guid.CreateVersion7(), "P-000001", "Cuaderno\nFALSO",
                "UNIDAD", 1, "MINORISTA", 20000m, "NINGUNO", null, null, 2000m, 20000m, 18000m)],
            [new SaleReceiptPaymentDto("EFECTIVO", 18000m, 20000m, 2000m)]);

        var text = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt, "Baratísimo", 32));

        Assert.Contains("V-000123INJECTADO", text, StringComparison.Ordinal);
        Assert.Contains("CuadernoFALSO", text, StringComparison.Ordinal);
        Assert.Contains("TOTAL", text, StringComparison.Ordinal);
        Assert.Contains("18.000", text, StringComparison.Ordinal);
        Assert.Contains("Efectivo", text, StringComparison.Ordinal);
        Assert.Contains("Cambio", text, StringComparison.Ordinal);
        Assert.Contains("Ticket de venta", text, StringComparison.Ordinal);
        Assert.Contains("¡Gracias por su compra!", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Atendió:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cliente:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nINJECTADO", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nFALSO", text, StringComparison.Ordinal);
        Assert.All(text.Split('\n').Where(line => line.Length > 0),
            line => Assert.True(line.TrimEnd('\r').Length <= 32, line));
    }

    [Fact]
    public void Render_ReprintAndHistoricalNamesUseOnlySnapshot()
    {
        var receipt = new SaleReceiptDto(Guid.CreateVersion7(), "V-000004",
            new DateTimeOffset(2026, 11, 6, 16, 30, 0, TimeSpan.Zero), new DateOnly(2026, 11, 6),
            "CREDITO", "CONFIRMADA", 5000m, 0m, 5000m, 0m, [], [], "Cajero histórico", "Cliente histórico");
        var output = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt, "Baratísimo", 32, true));
        Assert.Contains("REIMPRESIÓN", output);
        Assert.Contains("Atendió: Cajero histórico", output);
        Assert.Contains("Cliente: Cliente histórico", output);
        Assert.Contains("06/11/2026 11:30", output);
        Assert.Contains("Venta a crédito", output);
        Assert.DoesNotContain("Descuento", output);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(48)]
    public void Render_ShowsIncludedVatOnlyWithCompletePositiveBreakdown(int columns)
    {
        var receipt = new SaleReceiptDto(Guid.CreateVersion7(), "V-1", DateTimeOffset.UtcNow,
            new DateOnly(2026, 11, 11), "CONTADO", "CONFIRMADA", 11900m, 0m, 11900m, 0m,
            [], [new SaleReceiptPaymentDto("EFECTIVO", 11900m, 11900m, 0m)],
            VatPercentage: 19m, TaxableBaseTotal: 10000m, VatTotal: 1900m);

        var output = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt, "Baratísimo", columns));
        Assert.Contains("Base gravable", output);
        Assert.Contains("IVA 19 % incluido", output);
        Assert.True(output.IndexOf("TOTAL", StringComparison.Ordinal) <
            output.IndexOf("Base gravable", StringComparison.Ordinal));
        Assert.All(output.Split('\n').Where(line => line.Length > 0),
            line => Assert.True(line.TrimEnd('\r').Length <= columns, line));

        foreach (var hidden in new[]
                 {
                     receipt with { VatPercentage = null }, receipt with { VatPercentage = 0m },
                     receipt with { TaxableBaseTotal = null }, receipt with { VatTotal = null }
                 })
        {
            var text = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(hidden, "Baratísimo", columns));
            Assert.DoesNotContain("Base gravable", text);
            Assert.DoesNotContain("incluido", text);
            Assert.Contains("11.900", text);
        }
    }

    [Theory]
    [InlineData(23)]
    [InlineData(81)]
    public void Render_RejectsUnsupportedColumnCount(int columns)
    {
        var receipt = new SaleReceiptDto(Guid.Empty, "V-1", DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow), "CONTADO", "CONFIRMADA",
            0, 0, 0, 0, [], []);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReceiptTextRenderer.Render(receipt, "Baratísimo", columns));
    }
}

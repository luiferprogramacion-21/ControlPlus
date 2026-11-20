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
        Assert.Contains("Recibido", text, StringComparison.Ordinal);
        Assert.Contains("Devuelta", text, StringComparison.Ordinal);
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
    public void Render_GroupsProductQuantitiesAndAmountsWithColombianSeparators()
    {
        var receipt = new SaleReceiptDto(
            Guid.CreateVersion7(), "V-000123", new DateTimeOffset(2026, 11, 19, 14, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 11, 19), "CONTADO", "CONFIRMADA", 1522756m, 0m, 1522756m, 0m,
            [new SaleLineDto(Guid.CreateVersion7(), Guid.CreateVersion7(), "P-000001", "Cuaderno",
                "UNIDAD", 1234, "MINORISTA", 1234m, "NINGUNO", null, null, 0m, 1522756m, 1522756m)],
            [new SaleReceiptPaymentDto("EFECTIVO", 1522756m, 1522756m, 0m)]);

        var text = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt, "Baratísimo", 32));

        Assert.Contains("1.234 x $ 1.234", text, StringComparison.Ordinal);
        Assert.Contains("$ 1.522.756", text, StringComparison.Ordinal);
        Assert.Contains("V-000123", text, StringComparison.Ordinal);
        Assert.All(text.Split('\n').Where(line => line.Length > 0),
            line => Assert.True(line.TrimEnd('\r').Length <= 32, line));
    }

    [Fact]
    public void Render_ReprintAndHistoricalNamesUseOnlySnapshot()
    {
        var receipt = new SaleReceiptDto(Guid.CreateVersion7(), "V-000004",
            new DateTimeOffset(2026, 11, 6, 16, 30, 0, TimeSpan.Zero), new DateOnly(2026, 11, 6),
            "CREDITO", "CONFIRMADA", 5000m, 0m, 5000m, 0m, [], [], "Cajero histórico", "Cliente histórico",
            DueDate: new DateOnly(2026, 11, 30), EstablishmentName: "Tienda histórica",
            EstablishmentIdentification: "900-1", EstablishmentPhone: "555", EstablishmentAddress: "Ocaña",
            ReceiptHeader: "Compra local", ReceiptFooter: "Vuelva pronto");
        var output = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt,
            new ReceiptEstablishmentInfo("Nombre actual", "NIT actual", ReceiptFooter: "Pie actual"), 32, true));
        Assert.Contains("REIMPRESIÓN", output);
        Assert.Contains("Tienda histórica", output);
        Assert.DoesNotContain("Nombre actual", output);
        Assert.Contains("NIT: 900-1", output);
        Assert.Contains("Compra local", output);
        Assert.Contains("Atendió: Cajero histórico", output);
        Assert.Contains("Cliente: Cliente histórico", output);
        Assert.Contains("06/11/2026 11:30", output);
        Assert.Contains("Venta a crédito", output);
        Assert.Contains("Vence: 30/11/2026", output);
        Assert.Contains("Vuelva pronto", output);
        Assert.DoesNotContain("Pie actual", output);
        Assert.DoesNotContain("Descuento", output);
    }

    [Fact]
    public void Render_NequiAndMixedPayments_ShowMethodReceivedAndChange()
    {
        // 20 November 2026: every paid ticket says how it was paid and always shows Recibido and Devuelta.
        var nequi = new SaleReceiptDto(Guid.CreateVersion7(), "V-NEQUI",
            new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero), new DateOnly(2026, 11, 20),
            "CONTADO", "CONFIRMADA", 30000m, 0m, 30000m, 0m, [],
            [new SaleReceiptPaymentDto("NEQUI", 30000m, null, 0m)]);
        var single = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(nequi, "Baratísimo", 32, false));
        Assert.Contains("Medio de pago: Nequi", single);
        Assert.DoesNotMatch(@"Nequi\s+\$", single);
        Assert.Contains("Nequi", single);
        Assert.Matches(@"Recibido\s+\$ 30\.000", single);
        Assert.Matches(@"Devuelta\s+\$ 0", single);

        var mixed = nequi with { Payments = [new SaleReceiptPaymentDto("EFECTIVO", 20000m, 50000m, 30000m),
            new SaleReceiptPaymentDto("NEQUI", 10000m, null, 0m)], Change = 30000m };
        var both = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(mixed, "Baratísimo", 32, false));
        Assert.Contains("Medios de pago:", both);
        Assert.Matches(@"Recibido\s+\$ 60\.000", both);
        Assert.Matches(@"Devuelta\s+\$ 30\.000", both);
    }

    [Fact]
    public void Render_ReprintKeepsCashReceivedAndChange()
    {
        var receipt = new SaleReceiptDto(Guid.CreateVersion7(), "V-REPRINT-CASH",
            new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero), new DateOnly(2026, 11, 19),
            "CONTADO", "CONFIRMADA", 48000m, 0m, 48000m, 2000m, [],
            [new SaleReceiptPaymentDto("EFECTIVO", 48000m, 50000m, 2000m)]);

        var output = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt, "Baratísimo", 32, true));

        Assert.Contains("REIMPRESIÓN", output);
        Assert.Contains("TOTAL", output);
        Assert.Contains("Recibido", output);
        Assert.Contains("$ 50.000", output);
        Assert.Contains("Devuelta", output);
        Assert.Contains("$ 2.000", output);
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
        // Subtotal + IVA = TOTAL, in that order, with the price already including the VAT.
        Assert.DoesNotContain("Base gravable", output);
        var subtotal = output.IndexOf("Subtotal", StringComparison.Ordinal);
        var vat = output.IndexOf("IVA 19 %", StringComparison.Ordinal);
        var total = output.IndexOf("TOTAL", StringComparison.Ordinal);
        Assert.True(subtotal >= 0 && subtotal < vat && vat < total, output);
        Assert.Contains("10.000", output);
        Assert.Contains("1.900", output);
        Assert.Contains("11.900", output);
        Assert.DoesNotContain("incluido", output);
        Assert.DoesNotContain("Valor productos", output);
        Assert.All(output.Split('\n').Where(line => line.Length > 0),
            line => Assert.True(line.TrimEnd('\r').Length <= columns, line));

        foreach (var hidden in new[]
                 {
                     receipt with { VatPercentage = null }, receipt with { VatPercentage = 0m },
                     receipt with { TaxableBaseTotal = null }, receipt with { VatTotal = null }
                 })
        {
            var text = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(hidden, "Baratísimo", columns));
            Assert.DoesNotContain("IVA", text);
            Assert.DoesNotContain("incluido", text);
            Assert.Contains("Subtotal", text);
            Assert.Contains("11.900", text);
        }
    }

    [Fact]
    public void Render_WithVatAndDiscount_ShowsProductsValueDiscountSubtotalVatAndTotalInOrder()
    {
        var receipt = new SaleReceiptDto(Guid.CreateVersion7(), "V-2", DateTimeOffset.UtcNow,
            new DateOnly(2026, 11, 11), "CONTADO", "CONFIRMADA", 5500m, 500m, 5000m, 0m,
            [], [new SaleReceiptPaymentDto("EFECTIVO", 5000m, 5000m, 0m)],
            VatPercentage: 19m, TaxableBaseTotal: 4202m, VatTotal: 798m);

        var output = Encoding.UTF8.GetString(ReceiptTextRenderer.Render(receipt, "Baratísimo", 32));
        var order = new[] { "Valor productos", "Descuento", "Subtotal", "IVA 19 %", "TOTAL" }
            .Select(label => output.IndexOf(label, StringComparison.Ordinal)).ToArray();
        Assert.All(order, index => Assert.True(index >= 0, output));
        Assert.Equal(order.OrderBy(x => x), order);
        Assert.Contains("4.202", output);
        Assert.Contains("798", output);
        Assert.Contains("5.000", output);
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

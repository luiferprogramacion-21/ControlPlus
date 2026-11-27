using System.Text;
using ControlPlus.Api.Controllers;
using ControlPlus.Application.Establishments.Contracts;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.Suppliers.Contracts;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Auditoría S.19: el pedido se descarga como PDF con el estilo del reporte de ventas.</summary>
public sealed class PurchaseOrderPdfTests
{
    private static PurchaseOrderDto Order(int lines) => new(Guid.CreateVersion7(), Guid.CreateVersion7(), "ENVIADO", "PED", "CP-2026-01",
        6, "PED-CP-2026-01-6", 2, new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 23, 16, 30, 0, DateTimeKind.Utc), null,
        Enumerable.Range(1, lines).Select(i => new PurchaseOrderLineDto(Guid.CreateVersion7(), Guid.CreateVersion7(),
            $"BIS-{i:000000}", $"Producto número {i}", 1_000 + i, 0, 1_000 + i, 0, 0, null,
            i == 1 ? "Empacar por docena" : null, 0, 0, 0, "MANUAL")).ToArray(), null, null, null, null, null, "Ana Gómez");

    private static readonly SupplierDto Supplier = new(Guid.CreateVersion7(), "NIT", "900123456-7", "Distribuidora La Ejemplar",
        "3001234567", "Calle 10 # 5-20", "ventas@distribuidora.example", null, true, 1);

    private static string Render(PurchaseOrderDto order, EstablishmentDto? business) =>
        Encoding.Latin1.GetString(PurchaseOrderPdf.Render(order, Supplier, business, DateTimeOffset.UtcNow));

    [Fact]
    public void Pdf_ShowsBusinessSupplierProductsAndTotals()
    {
        var pdf = Render(Order(2), new EstablishmentDto("Baratísimo", "901234567", "3110000000", "Centro", null, null, 1));
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        foreach (var text in new[] { "Pedido al proveedor", "PED-CP-2026-01-6", "Baratísimo", "NIT 901234567", "Distribuidora La Ejemplar",
                     "NIT 900123456-7", "Producto número 1", "BIS-000001", "1.001", "Empacar por docena", "Total de unidades",
                     "2.003", "Enviado el 23/11/2026 a las 11:30", "Recibido por el proveedor", "Página 1 de 1" })
            Assert.Contains(text, pdf);
    }

    [Fact]
    public void Pdf_ManyProducts_UsesSeveralPagesAndRepeatsTheHeader()
    {
        var pdf = Render(Order(60), null);
        Assert.Contains("Página 1 de 3", pdf);
        Assert.Contains("Producto número 60", pdf);
        Assert.True(pdf.Split("(Cantidad) Tj").Length - 1 >= 3);
        // Sin datos del establecimiento el PDF dice ControlPlus; con datos usa el nombre configurado (27/11/2026).
        Assert.Contains("ControlPlus · Pedido PED-CP-2026-01-6", pdf);
        // Pedido del usuario del 27/11/2026: «Elaboró» lleva el nombre de quien hizo el pedido.
        Assert.Contains("Elaboró: Ana Gómez", pdf);
    }

    [Fact]
    public void FileName_IsSafeForWindows() =>
        Assert.Equal("pedido-PED-CP-2026-01-6.pdf", PurchaseOrderPdf.FileName(Order(1)));
}

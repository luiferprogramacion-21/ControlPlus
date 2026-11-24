using System.Globalization;
using ControlPlus.Application.Establishments.Contracts;
using ControlPlus.Application.PurchaseOrders.Contracts;
using ControlPlus.Application.Suppliers.Contracts;
using static ControlPlus.Api.Controllers.SalesOverviewPdf;

namespace ControlPlus.Api.Controllers;

/// <summary>
/// PDF del pedido al proveedor (auditoría del 23 de noviembre de 2026, S.19): mismo estilo que el reporte de ventas, con
/// los datos del negocio y del proveedor, la tabla de productos y las firmas. Se descarga al enviar el pedido.
/// </summary>
public static class PurchaseOrderPdf
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly TimeZoneInfo LocalZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Bogota");

    public static string FileName(PurchaseOrderDto order) =>
        $"pedido-{new string(order.VisibleNumber.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray())}.pdf";

    public static byte[] Render(PurchaseOrderDto order, SupplierDto supplier, EstablishmentDto? business, DateTimeOffset generatedAtUtc)
    {
        var businessName = string.IsNullOrWhiteSpace(business?.CommercialName) ? "Baratísimo" : business.CommercialName.Trim();
        var doc = new Document($"{businessName} · Pedido {order.VisibleNumber}");
        var generated = TimeZoneInfo.ConvertTime(generatedAtUtc, LocalZone);
        var sentAt = TimeZoneInfo.ConvertTime(new DateTimeOffset(DateTime.SpecifyKind(order.SentAt ?? order.CreatedAt, DateTimeKind.Utc)), LocalZone);

        // Encabezado, como el reporte de ventas.
        doc.Text(Margin, doc.Y - 10, Fit(businessName, 10, Right - Margin), 10, bold: true, color: Gray);
        doc.Y -= 16;
        var businessContact = string.Join(" · ", new[]
        {
            string.IsNullOrWhiteSpace(business?.Identification) ? null : $"NIT {business!.Identification!.Trim()}",
            business?.Phone?.Trim(), business?.Address?.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (businessContact.Length > 0)
        {
            doc.Text(Margin, doc.Y - 9, Fit(businessContact, 9, Right - Margin), 9, color: Gray);
            doc.Y -= 14;
        }
        doc.Text(Margin, doc.Y - 18, "Pedido al proveedor", 20, bold: true, color: Blue);
        doc.TextRight(Right, doc.Y - 18, order.VisibleNumber, 14, bold: true, color: Black);
        doc.Y -= 30;
        doc.Text(Margin, doc.Y - 12, $"Enviado el {sentAt.ToString("dd/MM/yyyy", Culture)} a las {sentAt.ToString("HH:mm", Culture)}", 12, color: Black);
        doc.Y -= 18;
        doc.Text(Margin, doc.Y - 9, $"Generado el {generated.ToString("dd/MM/yyyy", Culture)} a las {generated.ToString("HH:mm", Culture)}", 9, color: Gray);
        doc.Y -= 14;
        doc.Line(Margin, doc.Y, Right, Rule);
        doc.Y -= 14;

        // Tarjetas: proveedor y resumen.
        const double gap = 12, cardWidth = (Right - Margin - gap) / 2, cardHeight = 70;
        var units = order.Lines.Sum(x => x.RequestedQuantity);
        var supplierDocument = string.IsNullOrWhiteSpace(supplier.DocumentNumber)
            ? "Sin documento registrado" : $"{supplier.DocumentType ?? "Documento"} {supplier.DocumentNumber}";
        var supplierContact = string.Join(" · ", new[] { supplier.Phone, supplier.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        doc.Fill(Margin, doc.Y - cardHeight, cardWidth, cardHeight, CardFill);
        doc.Text(Margin + 10, doc.Y - 16, "Proveedor", 9, color: Gray);
        doc.Text(Margin + 10, doc.Y - 32, Fit(supplier.Name, 13, cardWidth - 20), 13, bold: true, color: Black);
        doc.Text(Margin + 10, doc.Y - 46, Fit(supplierDocument, 8, cardWidth - 20), 8, color: Gray);
        if (supplierContact.Length > 0) doc.Text(Margin + 10, doc.Y - 57, Fit(supplierContact, 8, cardWidth - 20), 8, color: Gray);
        if (!string.IsNullOrWhiteSpace(supplier.Address)) doc.Text(Margin + 10, doc.Y - 66, Fit(supplier.Address, 8, cardWidth - 20), 8, color: Gray);
        var summaryX = Margin + cardWidth + gap;
        doc.Fill(summaryX, doc.Y - cardHeight, cardWidth, cardHeight, CardFill);
        doc.Text(summaryX + 10, doc.Y - 16, "Lo que se pide", 9, color: Gray);
        doc.Text(summaryX + 10, doc.Y - 34, $"{units.ToString("N0", Culture)} {(units == 1 ? "unidad" : "unidades")}", 15, bold: true, color: Black);
        doc.Text(summaryX + 10, doc.Y - 48, $"{order.Lines.Count.ToString("N0", Culture)} {(order.Lines.Count == 1 ? "producto" : "productos")}", 8, color: Gray);
        doc.Y -= cardHeight + gap + 6;

        // Tabla de productos; la cabecera se repite si pasa de página.
        const double rowHeight = 30, quantityRight = 400;
        void Header()
        {
            doc.Fill(Margin, doc.Y - 18, Right - Margin, 18, HeaderFill);
            doc.Text(Margin + 8, doc.Y - 12, "#", 9, bold: true, color: Black);
            doc.Text(Margin + 32, doc.Y - 12, "Producto", 9, bold: true, color: Black);
            doc.TextRight(quantityRight, doc.Y - 12, "Cantidad", 9, bold: true, color: Black);
            doc.Text(quantityRight + 16, doc.Y - 12, "Observación", 9, bold: true, color: Black);
            doc.Y -= 18;
        }
        doc.EnsureSpace(18 + rowHeight);
        Header();
        var index = 0;
        foreach (var line in order.Lines)
        {
            doc.EnsureSpace(rowHeight, Header);
            index++;
            doc.Text(Margin + 8, doc.Y - 13, index.ToString("N0", Culture), 9, color: Gray);
            doc.Text(Margin + 32, doc.Y - 13, Fit(line.ProductName, 10, quantityRight - Margin - 110), 10, bold: true, color: Black);
            doc.Text(Margin + 32, doc.Y - 25, Fit(line.ProductCode, 8, quantityRight - Margin - 110), 8, color: Gray);
            doc.TextRight(quantityRight, doc.Y - 16, line.RequestedQuantity.ToString("N0", Culture), 12, bold: true, color: Black);
            doc.Text(quantityRight + 16, doc.Y - 16, Fit(string.IsNullOrWhiteSpace(line.Observation) ? "-" : line.Observation.Trim(), 8, Right - quantityRight - 24), 8, color: Gray);
            doc.Y -= rowHeight;
            doc.Line(Margin, doc.Y, Right, Rule);
        }
        doc.EnsureSpace(20, Header);
        doc.Text(Margin + 32, doc.Y - 13, "Total de unidades", 9, bold: true, color: Black);
        doc.TextRight(quantityRight, doc.Y - 14, units.ToString("N0", Culture), 12, bold: true, color: Black);
        doc.Y -= 40;

        // Firmas.
        doc.EnsureSpace(60);
        doc.Y -= 30;
        const double signatureWidth = 200;
        doc.Line(Margin, doc.Y, Margin + signatureWidth, Gray);
        doc.Line(Right - signatureWidth, doc.Y, Right, Gray);
        doc.Text(Margin, doc.Y - 12, "Elaboró", 9, color: Gray);
        doc.Text(Right - signatureWidth, doc.Y - 12, "Recibido por el proveedor", 9, color: Gray);
        return doc.ToPdf();
    }
}

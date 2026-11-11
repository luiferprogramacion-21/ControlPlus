using System.Globalization;
using System.Text;
using ControlPlus.Application.Reports;

namespace ControlPlus.Api.Controllers;

public static class SalesOverviewPdf
{
    private static readonly Encoding PdfEncoding = Encoding.Latin1;

    public static byte[] Render(SalesOverviewReport report)
    {
        var lines = new List<string>
        {
            "CONTROLPLUS - REPORTE DE VENTAS",
            $"Periodo operativo: {report.From:yyyy-MM-dd} a {report.To:yyyy-MM-dd}",
            $"Generado (UTC): {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}",
            "Moneda: COP. Importes cobrados con IVA incluido cuando corresponde; ganancia sobre base sin IVA.",
            "",
            $"Ventas originales: {Money(report.Sales.Total)} | Transacciones: {report.Sales.Transactions}",
            $"Contado: {Money(report.Sales.CashTotal)} | Crédito: {Money(report.Sales.CreditTotal)}",
            $"Ajuste neto por cambios: {Money(report.Changes.NetTotal)} ({report.Changes.Transactions} cambios)",
            $"Ventas netas: {Money(report.NetSalesTotal)}",
            $"Costo productos: {MoneyOrUnavailable(report.EstimatedCost)}",
            $"Ganancia estimada: {MoneyOrUnavailable(report.EstimatedMargin)}",
            $"Porcentaje de ganancia estimada: {PercentOrUnavailable(report.EstimatedMarginPercentage)}",
            $"Costo: {report.CostCompleteness}; líneas sin costo: {report.MissingCostLines}; sin movimiento: {report.MissingMovementLines}",
            $"Ticket promedio: {MoneyOrUnavailable(report.AverageTicket)}",
            $"Ventas período anterior: {Money(report.PreviousSalesTotal)}; variación: {PercentOrUnavailable(report.SalesVariationPercentage)}",
            "",
            "VENTAS POR FECHA OPERATIVA",
            "Fecha             Venta original       Cambios       Venta neta"
        };
        lines.AddRange(report.Days.Select(x =>
            $"{x.Date:yyyy-MM-dd}       {Money(x.SalesTotal),14}  {Money(x.ChangesNetTotal),13}  {Money(x.NetSalesTotal),14}"));
        lines.Add("");
        lines.Add("MEDIOS DE COBRO INMEDIATO - SOLO VENTAS DE CONTADO");
        lines.Add($"Total aplicado: {Money(report.PaymentMethodsTotal)}");
        lines.AddRange(report.PaymentMethods.Select(x =>
            $"{x.Name} ({x.Code}): {Money(x.Total)}; {PercentOrUnavailable(x.Percentage)}"));
        lines.Add("No incluye abonos, apartados ni cobros o devoluciones por cambios.");
        lines.Add("Ganancia estimada: no equivale a utilidad después de impuestos y gastos.");

        const int linesPerPage = 62;
        var pageCount = Math.Max(1, (lines.Count + linesPerPage - 1) / linesPerPage);
        var contents = Enumerable.Range(0, pageCount)
            .Select(page => Content(lines.Skip(page * linesPerPage).Take(linesPerPage), page + 1, pageCount))
            .ToArray();
        var offsets = new List<long> { 0 };
        using var output = new MemoryStream();
        Write(output, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        void Object(int id, string body)
        {
            while (offsets.Count <= id) offsets.Add(0);
            offsets[id] = output.Position;
            Write(output, $"{id} 0 obj\n{body}\nendobj\n");
        }
        Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{4 + 2 * i} 0 R"));
        Object(2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        for (var i = 0; i < pageCount; i++)
        {
            var pageId = 4 + 2 * i;
            var streamId = pageId + 1;
            Object(pageId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {streamId} 0 R >>");
            var contentBytes = PdfEncoding.GetBytes(contents[i]);
            Object(streamId, $"<< /Length {contentBytes.Length} >>\nstream\n{contents[i]}endstream");
        }
        var xref = output.Position;
        Write(output, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(output, $"{offset:D10} 00000 n \n");
        Write(output, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Content(IEnumerable<string> lines, int page, int total)
    {
        var result = new StringBuilder();
        var y = 805;
        foreach (var line in lines)
        {
            var safe = new string(line.Select(ch => ch is >= ' ' and <= '~' or >= '\u00a0' and <= '\u00ff' ? ch : '?').ToArray())
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal);
            result.Append("BT /F1 9 Tf 35 ").Append(y).Append(" Td (").Append(safe).Append(") Tj ET\n");
            y -= 12;
        }
        result.Append("BT /F1 8 Tf 35 30 Td (Página ").Append(page).Append(" de ").Append(total).Append(") Tj ET\n");
        return result.ToString();
    }

    private static void Write(Stream output, string text) => output.Write(PdfEncoding.GetBytes(text));
    private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string MoneyOrUnavailable(decimal? value) => value is decimal amount ? Money(amount) : "No disponible";
    private static string PercentOrUnavailable(decimal? value) => value is decimal amount ? $"{Money(amount)}%" : "Sin referencia";
}

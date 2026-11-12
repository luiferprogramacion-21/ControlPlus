using System.Globalization;
using System.Text;
using ControlPlus.Application.Reports;

namespace ControlPlus.Api.Controllers;

/// <summary>
/// PDF of the sales report written for the shop owner: plain Spanish, local dates, pesos with thousands separators
/// and no system codes. It uses only the built-in Helvetica fonts, so it needs no dependency or embedded font.
/// </summary>
public static class SalesOverviewPdf
{
    private static readonly Encoding PdfEncoding = Encoding.Latin1;
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-CO");
    private static readonly TimeZoneInfo LocalZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Bogota");

    private const double PageWidth = 595, PageHeight = 842, Margin = 40, BottomLimit = 62;
    private const double Right = PageWidth - Margin;
    private const string Blue = "0.157 0.369 0.659", Gray = "0.376 0.447 0.522", Black = "0.141 0.192 0.259",
        Amber = "0.522 0.318 0", HeaderFill = "0.910 0.929 0.953", CardFill = "0.945 0.957 0.973",
        WarningFill = "1 0.953 0.855", Rule = "0.78 0.82 0.86", Track = "0.91 0.93 0.95";

    // Helvetica glyph widths (1/1000 em) for ASCII 32..126; accented Latin-1 letters use their base width.
    private static readonly int[] Widths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    ];

    /// <summary>Pesos with thousands separators and the sign in front, e.g. «$ 1.234» or «-$ 500».</summary>
    public static string FormatMoney(decimal value) =>
        (value < 0 ? "-$ " : "$ ") + decimal.Round(Math.Abs(value), 0, MidpointRounding.AwayFromZero).ToString("N0", Culture);

    public static string FormatPercent(decimal value) => value.ToString("0.##", Culture) + " %";
    public static string FormatDate(DateOnly value) => value.ToString("dd/MM/yyyy", Culture);

    public static byte[] Render(SalesOverviewReport report)
    {
        var doc = new Document();
        var generated = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, LocalZone);
        var dayCount = report.To.DayNumber - report.From.DayNumber + 1;

        // Heading
        doc.Text(Margin, doc.Y - 10, "Baratísimo", 10, bold: true, color: Gray);
        doc.Y -= 16;
        doc.Text(Margin, doc.Y - 18, "Reporte de ventas", 20, bold: true, color: Blue);
        doc.Y -= 30;
        doc.Text(Margin, doc.Y - 12, $"Del {FormatDate(report.From)} al {FormatDate(report.To)}", 12, color: Black);
        doc.Y -= 18;
        doc.Text(Margin, doc.Y - 9, $"Generado el {generated.ToString("dd/MM/yyyy", Culture)} a las {generated.ToString("HH:mm", Culture)}", 9, color: Gray);
        doc.Y -= 14;
        doc.Line(Margin, doc.Y, Right, Rule);
        doc.Y -= 14;

        // Key figures
        var incomplete = report.CostCompleteness != "COMPLETE";
        var sales = report.Sales.Transactions;
        const double gap = 12, cardWidth = (Right - Margin - gap) / 2, cardHeight = 56;
        void Cards(params (string Label, string Value, string Sub)[] pair)
        {
            doc.EnsureSpace(cardHeight + gap);
            for (var i = 0; i < pair.Length; i++)
            {
                var x = Margin + i * (cardWidth + gap);
                doc.Fill(x, doc.Y - cardHeight, cardWidth, cardHeight, CardFill);
                doc.Text(x + 10, doc.Y - 16, pair[i].Label, 9, color: Gray);
                doc.Text(x + 10, doc.Y - 34, pair[i].Value, 15, bold: true, color: Black);
                doc.Text(x + 10, doc.Y - 48, Fit(pair[i].Sub, 8, cardWidth - 20), 8, color: Gray);
            }
            doc.Y -= cardHeight + gap;
        }
        Cards(
            ("Ventas del período", FormatMoney(report.Sales.Total),
                $"{sales} {(sales == 1 ? "venta" : "ventas")} · contado {FormatMoney(report.Sales.CashTotal)} · crédito {FormatMoney(report.Sales.CreditTotal)}"),
            ("Cambios de productos", FormatMoney(report.Changes.NetTotal),
                $"{report.Changes.Transactions} {(report.Changes.Transactions == 1 ? "cambio" : "cambios")} · lo entregado menos lo devuelto"));
        Cards(
            ("Total vendido", FormatMoney(report.NetSalesTotal), "Ventas del período más los cambios"),
            ("Venta promedio", Optional(report.AverageTicket), "Total de ventas entre el número de ventas"));
        Cards(
            ("Costo de los productos", Optional(report.EstimatedCost),
                incomplete ? "No se pudo calcular (ver aviso)" : "Lo que costó lo que se vendió"),
            ("Ganancia estimada", Optional(report.EstimatedMargin),
                report.EstimatedMarginPercentage is decimal pct ? $"{FormatPercent(pct)} de las ventas sin IVA" : "No se pudo calcular"));

        if (incomplete)
        {
            var missing = report.MissingCostLines + report.MissingMovementLines;
            var detail = Wrap($"Faltan los costos de {missing} {(missing == 1 ? "producto vendido" : "productos vendidos")}. " +
                              "Revisa que cada producto tenga su costo registrado.", 9, Right - Margin - 20);
            var height = 28 + detail.Count * 12;
            doc.EnsureSpace(height + 10);
            doc.Fill(Margin, doc.Y - height, Right - Margin, height, WarningFill);
            doc.Text(Margin + 10, doc.Y - 16, "Atención: no se pudo calcular el costo ni la ganancia.", 9, bold: true, color: Amber);
            for (var i = 0; i < detail.Count; i++) doc.Text(Margin + 10, doc.Y - 30 - i * 12, detail[i], 9, color: Amber);
            doc.Y -= height + 10;
        }

        var previousTo = report.From.AddDays(-1);
        var previousFrom = report.From.AddDays(-dayCount);
        string comparisonText;
        if (report.SalesVariationPercentage is not decimal variation)
        {
            comparisonText = "No hubo ventas en el período anterior para comparar.";
        }
        else
        {
            var movement = variation > 0 ? $"las ventas subieron {FormatPercent(variation)}."
                : variation < 0 ? $"las ventas bajaron {FormatPercent(Math.Abs(variation))}."
                : "las ventas se mantuvieron igual.";
            comparisonText = $"Frente al período anterior (del {FormatDate(previousFrom)} al {FormatDate(previousTo)}, " +
                             $"{FormatMoney(report.PreviousSalesTotal)}), {movement}";
        }
        foreach (var line in Wrap(comparisonText, 9, Right - Margin))
        {
            doc.EnsureSpace(14);
            doc.Text(Margin, doc.Y - 10, line, 9, color: Black);
            doc.Y -= 13;
        }
        doc.Y -= 10;

        // Sales per day
        const double dayRowHeight = 15;
        void DayHeader()
        {
            doc.Fill(Margin, doc.Y - 18, Right - Margin, 18, HeaderFill);
            doc.Text(Margin + 8, doc.Y - 12, "Fecha", 9, bold: true, color: Black);
            doc.TextRight(300, doc.Y - 12, "Ventas", 9, bold: true, color: Black);
            doc.TextRight(420, doc.Y - 12, "Cambios", 9, bold: true, color: Black);
            doc.TextRight(Right - 8, doc.Y - 12, "Total del día", 9, bold: true, color: Black);
            doc.Y -= 18;
        }
        Section(doc, "Ventas por día", dayRowHeight + 18 + 40);
        DayHeader();
        foreach (var day in report.Days)
        {
            doc.EnsureSpace(dayRowHeight, DayHeader);
            doc.Text(Margin + 8, doc.Y - 11, FormatDate(day.Date), 9, color: Black);
            doc.TextRight(300, doc.Y - 11, FormatMoney(day.SalesTotal), 9, color: Black);
            doc.TextRight(420, doc.Y - 11, FormatMoney(day.ChangesNetTotal), 9, color: Black);
            doc.TextRight(Right - 8, doc.Y - 11, FormatMoney(day.NetSalesTotal), 9, color: Black);
            doc.Y -= dayRowHeight;
            doc.Line(Margin, doc.Y, Right, Rule);
        }
        doc.EnsureSpace(dayRowHeight + 4, DayHeader);
        doc.Text(Margin + 8, doc.Y - 12, "Total", 9, bold: true, color: Black);
        doc.TextRight(300, doc.Y - 12, FormatMoney(report.Sales.Total), 9, bold: true, color: Black);
        doc.TextRight(420, doc.Y - 12, FormatMoney(report.Changes.NetTotal), 9, bold: true, color: Black);
        doc.TextRight(Right - 8, doc.Y - 12, FormatMoney(report.NetSalesTotal), 9, bold: true, color: Black);
        doc.Y -= dayRowHeight + 14;

        // Payment methods
        const double methodRowHeight = 18;
        Section(doc, "Cómo pagaron los clientes", 60);
        foreach (var line in Wrap("Solo ventas de contado. No incluye abonos de crédito, apartados ni el dinero cobrado o devuelto por cambios.", 8, Right - Margin))
        {
            doc.Text(Margin, doc.Y - 9, line, 8, color: Gray);
            doc.Y -= 11;
        }
        doc.Y -= 4;
        if (report.PaymentMethods.Count == 0)
        {
            doc.EnsureSpace(18);
            doc.Text(Margin, doc.Y - 11, "No hubo ventas de contado con pago en este período.", 9, color: Black);
            doc.Y -= 18;
        }
        else
        {
            void MethodHeader()
            {
                doc.Fill(Margin, doc.Y - 18, Right - Margin, 18, HeaderFill);
                doc.Text(Margin + 8, doc.Y - 12, "Medio de pago", 9, bold: true, color: Black);
                doc.TextRight(300, doc.Y - 12, "Total cobrado", 9, bold: true, color: Black);
                doc.TextRight(372, doc.Y - 12, "Porcentaje", 9, bold: true, color: Black);
                doc.Y -= 18;
            }
            MethodHeader();
            foreach (var method in report.PaymentMethods)
            {
                doc.EnsureSpace(methodRowHeight, MethodHeader);
                doc.Text(Margin + 8, doc.Y - 12, method.Name, 9, color: Black);
                doc.TextRight(300, doc.Y - 12, FormatMoney(method.Total), 9, color: Black);
                doc.TextRight(372, doc.Y - 12, FormatPercent(method.Percentage), 9, color: Black);
                const double barX = 392, barWidth = Right - 8 - barX;
                doc.Fill(barX, doc.Y - 13, barWidth, 7, Track);
                var share = Math.Clamp((double)method.Percentage / 100d, 0d, 1d);
                if (share > 0) doc.Fill(barX, doc.Y - 13, Math.Max(1, barWidth * share), 7, Blue);
                doc.Y -= methodRowHeight;
                doc.Line(Margin, doc.Y, Right, Rule);
            }
            doc.EnsureSpace(methodRowHeight, MethodHeader);
            doc.Text(Margin + 8, doc.Y - 12, "Total", 9, bold: true, color: Black);
            doc.TextRight(300, doc.Y - 12, FormatMoney(report.PaymentMethodsTotal), 9, bold: true, color: Black);
            doc.Y -= methodRowHeight;
        }
        doc.Y -= 14;

        // Notes
        var notes = new[]
        {
            "La ganancia es una estimación: las ventas y los cambios, sin el IVA que incluyen, menos lo que costaron los productos. No descuenta los gastos del negocio ni otros impuestos.",
            "Los cambios de productos se cuentan en el día en que se registraron, y no como una venta nueva."
        };
        foreach (var note in notes)
            foreach (var line in Wrap(note, 8, Right - Margin))
            {
                doc.EnsureSpace(11);
                doc.Text(Margin, doc.Y - 9, line, 8, color: Gray);
                doc.Y -= 11;
            }

        return doc.ToPdf();
    }

    private static string Optional(decimal? value) => value is decimal amount ? FormatMoney(amount) : "No disponible";

    private static void Section(Document doc, string title, double minimumSpace)
    {
        doc.EnsureSpace(minimumSpace);
        doc.Text(Margin, doc.Y - 13, title, 13, bold: true, color: Blue);
        doc.Y -= 22;
    }

    private static double Width(string text, double size, bool bold)
    {
        double total = 0;
        foreach (var ch in text)
        {
            double glyph = ch is >= ' ' and <= '~' ? Widths[ch - ' '] : char.IsUpper(ch) ? 667 : 556;
            total += bold && char.IsLetter(ch) ? glyph * 1.06 : glyph;
        }
        return total / 1000d * size;
    }

    private static string Fit(string text, double size, double maxWidth)
    {
        if (Width(text, size, false) <= maxWidth) return text;
        while (text.Length > 1 && Width(text + "...", size, false) > maxWidth) text = text[..^1];
        return text.TrimEnd() + "...";
    }

    private static List<string> Wrap(string text, double size, double maxWidth, bool bold = false)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current.ToString() + " " + word;
            if (current.Length > 0 && Width(candidate, size, bold) > maxWidth)
            {
                lines.Add(current.ToString());
                current.Clear().Append(word);
            }
            else
            {
                current.Clear().Append(candidate);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static string Escape(string text)
    {
        var safe = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var printable = ch is >= ' ' and <= '~' or >= ' ' and <= 'ÿ' ? ch : '?';
            if (printable is '\\' or '(' or ')') safe.Append('\\');
            safe.Append(printable);
        }
        return safe.ToString();
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed class Document
    {
        private readonly List<StringBuilder> _pages = [];
        private StringBuilder _page = null!;
        public double Y { get; set; }

        public Document() => NewPage();

        private void NewPage()
        {
            _page = new StringBuilder();
            _pages.Add(_page);
            Y = PageHeight - Margin;
        }

        /// <summary>Starts a new page (repeating the optional table header) when the next block does not fit.</summary>
        public void EnsureSpace(double height, Action? repeatHeader = null)
        {
            if (Y - height >= BottomLimit) return;
            NewPage();
            repeatHeader?.Invoke();
        }

        public void Text(double x, double baseline, string text, double size, bool bold = false, string color = Black) =>
            _page.Append("BT ").Append(color).Append(" rg /").Append(bold ? "F2 " : "F1 ").Append(Number(size))
                .Append(" Tf ").Append(Number(x)).Append(' ').Append(Number(baseline)).Append(" Td (")
                .Append(Escape(text)).Append(") Tj ET\n");

        public void TextRight(double right, double baseline, string text, double size, bool bold = false, string color = Black) =>
            Text(right - Width(text, size, bold), baseline, text, size, bold, color);

        public void Fill(double x, double y, double width, double height, string color) =>
            _page.Append(color).Append(" rg ").Append(Number(x)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(width)).Append(' ').Append(Number(height)).Append(" re f\n");

        public void Line(double x1, double y, double x2, string color) =>
            _page.Append(color).Append(" RG 0.5 w ").Append(Number(x1)).Append(' ').Append(Number(y)).Append(" m ")
                .Append(Number(x2)).Append(' ').Append(Number(y)).Append(" l S\n");

        public byte[] ToPdf()
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                _page = _pages[i];
                Line(Margin, 44, Right, Rule);
                Text(Margin, 30, "Baratísimo · Reporte de ventas", 8, color: Gray);
                TextRight(Right, 30, $"Página {i + 1} de {_pages.Count}", 8, color: Gray);
            }

            var offsets = new List<long> { 0 };
            using var output = new MemoryStream();
            void Write(string text) => output.Write(PdfEncoding.GetBytes(text));
            void Object(int id, string body)
            {
                while (offsets.Count <= id) offsets.Add(0);
                offsets[id] = output.Position;
                Write($"{id} 0 obj\n{body}\nendobj\n");
            }

            Write("%PDF-1.4\n%âãÏÓ\n");
            Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(i => $"{5 + 2 * i} 0 R"));
            Object(2, $"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
            Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            Object(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            for (var i = 0; i < _pages.Count; i++)
            {
                var pageId = 5 + 2 * i;
                var content = _pages[i].ToString();
                Object(pageId, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(PageWidth)} {Number(PageHeight)}] " +
                               $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {pageId + 1} 0 R >>");
                Object(pageId + 1, $"<< /Length {PdfEncoding.GetBytes(content).Length} >>\nstream\n{content}endstream");
            }

            var xref = output.Position;
            Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1)) Write($"{offset:D10} 00000 n \n");
            Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return output.ToArray();
        }
    }
}

using System.Globalization;
using System.Text;
using ControlPlus.Application.Sales.Contracts;

namespace ControlPlus.Application.Printing;

/// <summary>Renders only the immutable, public receipt data. The printer driver controls the device.</summary>
public static class ReceiptTextRenderer
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    public static byte[] Render(SaleReceiptDto receipt, string storeName, int columns, bool reprint = false)
        => Render(receipt, new ReceiptEstablishmentInfo(storeName), columns, reprint);

    public static byte[] Render(SaleReceiptDto receipt, ReceiptEstablishmentInfo establishment,
        int columns, bool reprint = false)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(establishment);
        if (columns is < 24 or > 80) throw new ArgumentOutOfRangeException(nameof(columns));
        var storeName = receipt.EstablishmentName ?? establishment.Name;
        if (string.IsNullOrWhiteSpace(storeName))
            throw new ArgumentException("Store name is required.", nameof(establishment));

        var text = new StringBuilder();
        AppendBlock(text, receipt.ReceiptHeader ?? establishment.ReceiptHeader, columns, centered: true);
        AppendCentered(text, Clean(storeName), columns);
        AppendBusinessLine(text, "NIT: ", receipt.EstablishmentIdentification ?? establishment.Identification, columns);
        AppendBusinessLine(text, string.Empty, receipt.EstablishmentAddress ?? establishment.Address, columns);
        AppendBusinessLine(text, "Tel: ", receipt.EstablishmentPhone ?? establishment.Phone, columns);
        AppendCentered(text, reprint ? "REIMPRESIÓN" : "Ticket de venta", columns);
        AppendRule(text, columns);
        foreach (var part in Wrap(Clean(receipt.VisibleNumber), columns))
            text.AppendLine(part);
        var localDate = TimeZoneInfo.ConvertTime(receipt.OccurredAtUtc, ReceiptTimeZone);
        text.AppendLine(localDate.ToString("dd/MM/yyyy HH:mm", ColombianCulture));
        if (!string.IsNullOrWhiteSpace(receipt.OperatorName))
            foreach (var part in Wrap("Atendió: " + Clean(receipt.OperatorName), columns)) text.AppendLine(part);
        if (!string.IsNullOrWhiteSpace(receipt.CustomerName))
            foreach (var part in Wrap("Cliente: " + Clean(receipt.CustomerName), columns)) text.AppendLine(part);
        AppendRule(text, columns);

        foreach (var line in receipt.Lines)
        {
            foreach (var part in Wrap(Clean(line.ProductName), columns))
                text.AppendLine(part);
            AppendAmount(text, $"{line.Quantity.ToString("N0", ColombianCulture)} x {Money(line.UnitPrice)}", line.NetAmount, columns);
        }

        AppendRule(text, columns);
        // The VAT breakdown appears only for sales that saved it (made with VAT switched on). The price already
        // includes the VAT, so the reading is: Subtotal + IVA = TOTAL, and the total never changes.
        var showVat = receipt.VatPercentage is > 0 && receipt.TaxableBaseTotal is not null && receipt.VatTotal is not null;
        if (showVat)
        {
            if (receipt.DiscountTotal > 0)
            {
                AppendAmount(text, "Valor productos", receipt.GrossSubtotal, columns);
                AppendAmount(text, "Descuento", -receipt.DiscountTotal, columns);
            }
            AppendAmount(text, "Subtotal", receipt.TaxableBaseTotal!.Value, columns);
            AppendAmount(text, $"IVA {receipt.VatPercentage!.Value.ToString("G29", ColombianCulture)} %", receipt.VatTotal!.Value, columns);
        }
        else
        {
            AppendAmount(text, "Subtotal", receipt.GrossSubtotal, columns);
            if (receipt.DiscountTotal > 0) AppendAmount(text, "Descuento", -receipt.DiscountTotal, columns);
        }
        AppendAmount(text, "TOTAL", receipt.Total, columns);
        AppendRule(text, columns);
        if (receipt.Payments.Count == 0)
        {
            AppendCentered(text, "Venta a crédito", columns);
            if (receipt.DueDate is { } dueDate)
                AppendCentered(text, $"Vence: {dueDate:dd/MM/yyyy}", columns);
        }
        // 20 November 2026 (requested by the user): every paid ticket says how it was paid and always shows Recibido
        // and Devuelta. Cash counts what the customer handed over; Nequi and transfers count their exact amount.
        if (receipt.Payments.Count > 0)
        {
            // One method: its name only, since its amount is the TOTAL above (the user's layout). Mixed: each amount.
            if (receipt.Payments.Count == 1)
                AppendBlock(text, "Medio de pago: " + PaymentName(Clean(receipt.Payments.First().MethodCode)), columns, centered: false);
            else
            {
                text.AppendLine("Medios de pago:");
                foreach (var payment in receipt.Payments)
                    AppendAmount(text, PaymentName(Clean(payment.MethodCode)), payment.AppliedAmount, columns);
            }
            AppendAmount(text, "Recibido", receipt.Payments.Sum(payment => payment.ReceivedAmount ?? payment.AppliedAmount), columns);
            AppendAmount(text, "Devuelta", receipt.Change, columns);
        }
        text.AppendLine();
        var footer = receipt.ReceiptFooter ?? establishment.ReceiptFooter;
        if (string.IsNullOrWhiteSpace(footer)) AppendCentered(text, "¡Gracias por su compra!", columns);
        else AppendBlock(text, footer, columns, centered: true);
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>
    /// Comprobante de una corrección (cambio, quitar producto) para la impresora de tickets (pruebas del usuario del
    /// 26/11/2026): el mismo contenido que el comprobante del navegador, en texto. La venta original no cambia.
    /// </summary>
    public static byte[] RenderCorrection(SaleCorrectionReceiptDto receipt, ReceiptEstablishmentInfo establishment, int columns)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(establishment);
        if (columns is < 24 or > 80) throw new ArgumentOutOfRangeException(nameof(columns));
        var correction = receipt.Correction;
        var storeName = string.IsNullOrWhiteSpace(establishment.Name) ? receipt.EstablishmentName : establishment.Name;
        var text = new StringBuilder();
        AppendCentered(text, Clean(storeName), columns);
        AppendBusinessLine(text, "NIT: ", establishment.Identification, columns);
        AppendCentered(text, "CORRECCIÓN " + Clean(correction.VisibleNumber), columns);
        AppendCentered(text, "Venta " + Clean(receipt.SaleNumber), columns);
        AppendCentered(text, TimeZoneInfo.ConvertTime(correction.OccurredAtUtc, ReceiptTimeZone)
            .ToString("dd/MM/yyyy HH:mm", ColombianCulture), columns);
        if (!string.IsNullOrWhiteSpace(correction.Reason))
            foreach (var part in Wrap("Motivo: " + Clean(correction.Reason), columns)) text.AppendLine(part);
        AppendRule(text, columns);
        foreach (var line in correction.Details)
        {
            var sign = line.Type == "DEVUELTO" ? "-" : "+";
            foreach (var part in Wrap($"{sign} {line.Quantity.ToString("N0", ColombianCulture)} {Clean(line.ProductName ?? line.ProductCode ?? "Producto")}", columns))
                text.AppendLine(part);
            AppendAmount(text, string.Empty, line.NetAmount, columns);
        }
        AppendRule(text, columns);
        if (correction.Difference == 0) AppendAmount(text, "Sin diferencia", 0, columns);
        else AppendAmount(text, correction.Difference < 0 ? "Se devuelve" : "Se cobra", Math.Abs(correction.Difference), columns);
        foreach (var payment in correction.Payments)
            AppendAmount(text, PaymentName(Clean(payment.MethodCode)), payment.Amount, columns);
        if (correction.CreditAdjustment > 0)
            AppendAmount(text, correction.Difference < 0 ? "Baja la deuda" : "Sube la deuda", correction.CreditAdjustment, columns);
        AppendRule(text, columns);
        AppendCentered(text, "La venta original se conserva.", columns);
        text.AppendLine();
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static void AppendBusinessLine(StringBuilder text, string prefix, string? value, int columns)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var part in Wrap(prefix + Clean(value), columns)) text.AppendLine(part);
    }

    private static void AppendBlock(StringBuilder text, string? value, int columns, bool centered)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        foreach (var part in Wrap(Clean(line.Trim()), columns))
        {
            if (centered) text.AppendLine(part.PadLeft((columns + part.Length) / 2));
            else text.AppendLine(part);
        }
    }

    private static string Clean(string value) => new(value.Where(c => !char.IsControl(c)).ToArray());

    private static TimeZoneInfo ReceiptTimeZone => TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Bogota");

    private static string Money(decimal amount) => "$ " + decimal.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", ColombianCulture);

    private static string PaymentName(string code) => code switch
    {
        "EFECTIVO" => "Efectivo", "TRANSFERENCIA" => "Transferencia", "NEQUI" => "Nequi", _ => code
    };

    private static IEnumerable<string> Wrap(string value, int columns)
    {
        for (var start = 0; start < value.Length; start += columns)
            yield return value.Substring(start, Math.Min(columns, value.Length - start));
        if (value.Length == 0) yield return string.Empty;
    }

    private static void AppendCentered(StringBuilder text, string value, int columns)
    {
        foreach (var part in Wrap(value, columns))
            text.AppendLine(part.PadLeft((columns + part.Length) / 2));
    }

    private static void AppendRule(StringBuilder text, int columns) => text.AppendLine(new string('-', columns));

    private static void AppendAmount(StringBuilder text, string label, decimal amount, int columns)
    {
        var value = Money(amount);
        if (value.Length >= columns)
        {
            text.AppendLine(label);
            foreach (var part in Wrap(value, columns)) text.AppendLine(part);
            return;
        }
        var safeLabel = label.Length > columns - value.Length - 1
            ? label[..Math.Max(1, columns - value.Length - 1)] : label;
        text.AppendLine(safeLabel + value.PadLeft(columns - safeLabel.Length));
    }
}

public sealed record ReceiptEstablishmentInfo(
    string Name,
    string? Identification = null,
    string? Phone = null,
    string? Address = null,
    string? ReceiptHeader = null,
    string? ReceiptFooter = null);

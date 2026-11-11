using System.Globalization;
using System.Text;
using ControlPlus.Application.Sales.Contracts;

namespace ControlPlus.Application.Printing;

/// <summary>Renders only the immutable, public receipt data. The printer driver controls the device.</summary>
public static class ReceiptTextRenderer
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    public static byte[] Render(SaleReceiptDto receipt, string storeName, int columns, bool reprint = false)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (columns is < 24 or > 80) throw new ArgumentOutOfRangeException(nameof(columns));
        if (string.IsNullOrWhiteSpace(storeName)) throw new ArgumentException("Store name is required.", nameof(storeName));

        var text = new StringBuilder();
        AppendCentered(text, "Baratísimo", columns);
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
            AppendAmount(text, $"{line.Quantity} x {Money(line.UnitPrice)}", line.NetAmount, columns);
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
        if (receipt.Payments.Count == 0) AppendCentered(text, "Venta a crédito", columns);
        foreach (var payment in receipt.Payments)
            AppendAmount(text, PaymentName(Clean(payment.MethodCode)), payment.AppliedAmount, columns);
        if (receipt.Change > 0) AppendAmount(text, "Cambio", receipt.Change, columns);
        text.AppendLine();
        AppendCentered(text, "¡Gracias por su compra!", columns);
        return Encoding.UTF8.GetBytes(text.ToString());
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

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ControlPlus.Application.Printing;

namespace ControlPlus.Infrastructure.Printing;

/// <summary>Uses the installed Windows printer driver; no printer language or brand is assumed.</summary>
public sealed class WindowsGdiPrintSpooler : IPrintSpooler
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    public Task SubmitAsync(string queueName, byte[] utf8Text, string documentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(utf8Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La impresión local requiere Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Submit(queueName, utf8Text, documentName), cancellationToken);
    }

    public Task SubmitProductLabelsAsync(string queueName, ProductLabelPrintPayload payload,
        ProductLabelPrintLayout layout, string documentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La impresión local requiere Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => SubmitLabels(queueName, payload, layout, documentName),
            cancellationToken);
    }

    private static void Submit(string queueName, byte[] utf8Text, string documentName)
    {
        var device = CreateDC("WINSPOOL", queueName, null, IntPtr.Zero);
        if (device == IntPtr.Zero)
            throw new PrintSubmissionException(false, new Win32Exception(Marshal.GetLastWin32Error()));
        var documentStarted = false;
        var font = IntPtr.Zero;
        var oldFont = IntPtr.Zero;
        try
        {
            var info = new DocumentInfo { Size = Marshal.SizeOf<DocumentInfo>(), Name = documentName };
            if (StartDoc(device, ref info) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            documentStarted = true;

            var dpi = GetDeviceCaps(device, 90); // LOGPIXELSY
            var pageHeight = GetDeviceCaps(device, 10); // VERTRES
            var pageWidth = GetDeviceCaps(device, 8); // HORZRES
            if (dpi <= 0 || pageHeight <= 0 || pageWidth <= 0)
                throw new InvalidOperationException("El controlador no informó dimensiones imprimibles.");
            var lines = Encoding.UTF8.GetString(utf8Text).Replace("\r", "", StringComparison.Ordinal).Split('\n');
            var longest = Math.Max(24, lines.Max(x => x.Length));
            var fontHeight = Math.Clamp(pageWidth * 10 / Math.Max(1, longest * 6),
                Math.Max(1, dpi * 7 / 72), Math.Max(1, dpi * 12 / 72));
            font = CreateFont(-fontHeight, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 0, 0, "Consolas");
            if (font == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            oldFont = SelectObject(device, font);
            if (oldFont == IntPtr.Zero || oldFont == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var margin = Math.Max(2, dpi / 16);
            var lineHeight = Math.Max(fontHeight + 2, dpi / 8);
            var y = pageHeight;
            foreach (var line in lines)
            {
                if (y + lineHeight > pageHeight)
                {
                    if (y != pageHeight && EndPage(device) <= 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (StartPage(device) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    y = margin;
                }
                if (line.Length > 0 && !TextOut(device, margin, y, line, line.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                y += lineHeight;
            }
            if (y != pageHeight && EndPage(device) <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (EndDoc(device) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            documentStarted = false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new PrintSubmissionException(documentStarted, exception);
        }
        finally
        {
            if (oldFont != IntPtr.Zero && oldFont != new IntPtr(-1)) SelectObject(device, oldFont);
            if (font != IntPtr.Zero) DeleteObject(font);
            if (documentStarted) _ = AbortDoc(device);
            DeleteDC(device);
        }
    }

    private const decimal DefaultLabelHeightMm = 40m;

    private static void SubmitLabels(string queueName, ProductLabelPrintPayload payload,
        ProductLabelPrintLayout layout, string documentName)
    {
        if (payload.Items.Count == 0 || payload.Items.Any(item => item.Quantity <= 0 ||
            !Code128Encoder.CanEncode(item.Barcode)))
            throw new ArgumentException("El lote de etiquetas no es válido.", nameof(payload));

        var device = CreateDC("WINSPOOL", queueName, null, IntPtr.Zero);
        if (device == IntPtr.Zero)
            throw new PrintSubmissionException(false, new Win32Exception(Marshal.GetLastWin32Error()));
        var documentStarted = false;
        try
        {
            var info = new DocumentInfo { Size = Marshal.SizeOf<DocumentInfo>(), Name = documentName };
            if (StartDoc(device, ref info) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            documentStarted = true;

            var pageWidth = GetDeviceCaps(device, 8); // HORZRES
            var pageHeight = GetDeviceCaps(device, 10); // VERTRES
            var dpiX = layout.ResolutionDpi ?? GetDeviceCaps(device, 88); // LOGPIXELSX
            var dpiY = layout.ResolutionDpi ?? GetDeviceCaps(device, 90); // LOGPIXELSY
            if (pageWidth <= 0 || pageHeight <= 0 || dpiX <= 0 || dpiY <= 0)
                throw new InvalidOperationException("El controlador no informó dimensiones imprimibles.");
            var labelWidth = layout.WidthMm is decimal width
                ? Math.Min(pageWidth, Math.Max(1, (int)decimal.Round(width * dpiX / 25.4m)))
                : pageWidth;
            // Without a configured height (a receipt roll), the driver reports a page as tall as a sheet and every label
            // stretched to fill it (20 November 2026). A label is then 40 mm tall; a configured height still wins.
            var labelHeight = Math.Min(pageHeight, Math.Max(1,
                (int)decimal.Round((layout.HeightMm ?? DefaultLabelHeightMm) * dpiY / 25.4m)));

            foreach (var item in payload.Items)
            for (var copy = 0; copy < item.Quantity; copy++)
            {
                if (StartPage(device) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                DrawLabel(device, item, payload.EstablishmentName, labelWidth, labelHeight, dpiY);
                if (EndPage(device) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (EndDoc(device) <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            documentStarted = false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new PrintSubmissionException(documentStarted, exception);
        }
        finally
        {
            if (documentStarted) _ = AbortDoc(device);
            DeleteDC(device);
        }
    }

    private static void DrawLabel(IntPtr device, ProductLabelPrintItem item, string? establishmentName,
        int width, int height, int dpiY)
    {
        var margin = Math.Max(2, Math.Min(width, height) / 25);
        var titleHeight = Math.Clamp(height / 10, Math.Max(8, dpiY * 7 / 72),
            Math.Max(9, dpiY * 13 / 72));
        var priceHeight = Math.Clamp(height / 7, Math.Max(10, dpiY * 9 / 72),
            Math.Max(12, dpiY * 18 / 72));
        _ = SetBkMode(device, 1); // TRANSPARENT
        var price = "$ " + decimal.Round(item.RetailPrice, 0, MidpointRounding.AwayFromZero)
            .ToString("N0", ColombianCulture);
        // Measure the actual selected font so the business and price never overlap.
        var priceWidth = MeasureText(device, price, priceHeight, "Arial");
        if (priceWidth > width - margin * 2)
            throw new InvalidOperationException("El precio no cabe de forma legible en el ancho de la etiqueta.");
        var priceX = width - margin - priceWidth;
        DrawText(device, priceX, margin, price, priceHeight, 700, "Arial");
        if (!string.IsNullOrWhiteSpace(establishmentName))
        {
            var business = FitText(establishmentName, Math.Max(0, priceX - margin * 2), titleHeight);
            while (business.Length > 0 && MeasureText(device, business, titleHeight, "Arial") > priceX - margin * 2)
                business = business.Length <= 2 ? "" : business[..^2] + "…";
            DrawText(device, margin, margin, business, titleHeight, 700, "Arial");
        }
        DrawText(device, margin, margin + priceHeight + Math.Max(1, margin / 2),
            FitText(item.Name, width - margin * 2, titleHeight), titleHeight, 400, "Arial");

        var widths = Code128Encoder.Widths(item.Barcode);
        const int quietModules = 10;
        var totalModules = widths.Sum() + quietModules * 2;
        var availableWidth = width - margin * 2;
        var moduleWidth = availableWidth / totalModules;
        if (moduleWidth < 1)
            throw new InvalidOperationException("El código no cabe en el ancho configurado para la etiqueta.");
        var barcodeWidth = totalModules * moduleWidth;
        var x = Math.Max(margin, (width - barcodeWidth) / 2) + quietModules * moduleWidth;
        var barcodeTop = margin + titleHeight + priceHeight + margin;
        var codeTextHeight = Math.Clamp(height / 12, Math.Max(7, dpiY * 6 / 72),
            Math.Max(8, dpiY * 10 / 72));
        var barcodeHeight = height - barcodeTop - codeTextHeight - margin * 2;
        if (barcodeHeight < Math.Max(8, dpiY / 8))
            throw new InvalidOperationException("El alto configurado no permite una etiqueta legible.");
        for (var index = 0; index < widths.Count; index++)
        {
            var elementWidth = widths[index] * moduleWidth;
            if (index % 2 == 0 && !PatBlt(device, x, barcodeTop, elementWidth,
                    barcodeHeight, 0x00000042)) // BLACKNESS
                throw new Win32Exception(Marshal.GetLastWin32Error());
            x += elementWidth;
        }
        var text = FitText(item.Barcode, width - margin * 2, codeTextHeight);
        var textWidth = Math.Max(1, text.Length * codeTextHeight * 3 / 5);
        DrawText(device, Math.Max(margin, (width - textWidth) / 2),
            barcodeTop + barcodeHeight + Math.Max(1, margin / 2), text,
            codeTextHeight, 400, "Consolas");
    }

    private static string FitText(string text, int width, int fontHeight)
    {
        if (width <= 0) return "";
        var maximum = Math.Max(1, width * 5 / Math.Max(1, fontHeight * 3));
        return text.Length <= maximum ? text : maximum == 1 ? "…" : text[..(maximum - 1)] + "…";
    }

    private static int MeasureText(IntPtr device, string text, int height, string faceName)
    {
        var font = CreateFont(-height, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 0, 0, faceName);
        if (font == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var oldFont = SelectObject(device, font);
        if (oldFont == IntPtr.Zero || oldFont == new IntPtr(-1))
        {
            DeleteObject(font);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            if (!GetTextExtentPoint32(device, text, text.Length, out var size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return size.Width;
        }
        finally { SelectObject(device, oldFont); DeleteObject(font); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextSize { public int Width; public int Height; }

    [DllImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextExtentPoint32(IntPtr device, string text, int length, out TextSize size);

    private static void DrawText(IntPtr device, int x, int y, string text,
        int height, int weight, string faceName)
    {
        var font = CreateFont(-height, 0, 0, 0, weight, 0, 0, 0, 0, 0, 0, 0, 0, faceName);
        if (font == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var oldFont = SelectObject(device, font);
        if (oldFont == IntPtr.Zero || oldFont == new IntPtr(-1))
        {
            DeleteObject(font);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        try
        {
            if (text.Length > 0 && !TextOut(device, x, y, text, text.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            SelectObject(device, oldFont);
            DeleteObject(font);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocumentInfo
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Output;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
        public int Type;
    }

    [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr initData);
    [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDoc(IntPtr device, ref DocumentInfo document);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int StartPage(IntPtr device);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int EndPage(IntPtr device);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int EndDoc(IntPtr device);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int AbortDoc(IntPtr device);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr device);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int GetDeviceCaps(IntPtr device, int index);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int SetBkMode(IntPtr device, int mode);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PatBlt(IntPtr device, int x, int y, int width, int height, uint operation);
    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr device, IntPtr value);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll", EntryPoint = "TextOutW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TextOut(IntPtr device, int x, int y, string text, int length);
}

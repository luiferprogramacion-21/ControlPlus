using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ControlPlus.Application.Printing;

namespace ControlPlus.Infrastructure.Printing;

/// <summary>Uses the installed Windows printer driver; no printer language or brand is assumed.</summary>
public sealed class WindowsGdiPrintSpooler : IPrintSpooler
{
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
            if (documentStarted) AbortDoc(device);
            DeleteDC(device);
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

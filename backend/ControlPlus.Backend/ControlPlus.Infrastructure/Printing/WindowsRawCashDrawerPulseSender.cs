using System.ComponentModel;
using System.Runtime.InteropServices;
using ControlPlus.Application.Printing;

namespace ControlPlus.Infrastructure.Printing;

/// <summary>
/// Sends only the ESC/POS drawer command through the Windows RAW print path. Receipt
/// and label jobs use their existing GDI path and never contain a drawer pulse.
/// </summary>
public sealed class WindowsRawCashDrawerPulseSender : ICashDrawerPulseSender
{
    public Task SendPulseAsync(string queueName, CashDrawerPulse pulse,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        if (queueName.Contains('\0')) throw new ArgumentException("Nombre de cola no válido.", nameof(queueName));
        ArgumentNullException.ThrowIfNull(pulse);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("La apertura local del cajón requiere Windows.");
        cancellationToken.ThrowIfCancellationRequested();

        // Winspool calls are synchronous. Once scheduled, cancellation cannot safely
        // distinguish a queued pulse from an unsent pulse, so it does not abort the job.
        return Task.Run(() => Submit(queueName, pulse));
    }

    private static void Submit(string queueName, CashDrawerPulse pulse)
    {
        if (!OpenPrinter(queueName, out var printer, IntPtr.Zero))
            throw new CashDrawerPulseSubmissionException(false,
                new Win32Exception(Marshal.GetLastWin32Error()));

        var documentStarted = false;
        try
        {
            var document = new RawDocumentInfo
            {
                DocumentName = "ControlPlus - Apertura de cajón",
                DataType = "RAW"
            };
            if (StartDocPrinter(printer, 1, ref document) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            documentStarted = true;

            if (!StartPagePrinter(printer))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var command = pulse.ToEscPosBytes();
            if (!WritePrinter(printer, command, command.Length, out var written))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (written != command.Length)
                throw new IOException("Windows no aceptó todos los bytes del pulso.");

            if (!EndPagePrinter(printer))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!EndDocPrinter(printer))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            documentStarted = false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new CashDrawerPulseSubmissionException(documentStarted, exception);
        }
        finally
        {
            // Abort can no longer undo a pulse already delivered by the spooler.
            if (documentStarted) AbortPrinter(printer);
            ClosePrinter(printer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RawDocumentInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer,
        IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint StartDocPrinter(IntPtr printer, uint level,
        ref RawDocumentInfo document);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(IntPtr printer, byte[] data, int count,
        out int written);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AbortPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printer);
}

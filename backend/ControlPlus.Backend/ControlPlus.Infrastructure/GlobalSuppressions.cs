using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Interoperability", "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
    Justification = "La ruta de impresión Windows conserva DllImport por compatibilidad comprobada con estructuras GDI y Winspool; su modernización exige validación con impresora física.")]

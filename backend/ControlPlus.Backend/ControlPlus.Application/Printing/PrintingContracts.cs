using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using System.Text.Json.Serialization;

namespace ControlPlus.Application.Printing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreatePrintProfileRequest(string Name, string SystemQueueName,
    int Columns, bool MakeDefault = true, string PrinterType = "RECIBOS",
    decimal? LabelWidthMm = null, decimal? LabelHeightMm = null, int? ResolutionDpi = null);

public sealed record PrintProfileDto(Guid Id, string Name, string SystemQueueName,
    int Columns, bool IsDefault, bool Active, long Version, string PrinterType,
    decimal? LabelWidthMm, decimal? LabelHeightMm, int? ResolutionDpi,
    bool DrawerEnabled = false, int? DrawerPin = null,
    int DrawerPulseOnMs = 100, int DrawerPulseOffMs = 100);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdatePrintProfileDrawerRequest(bool Enabled, int? Pin,
    int PulseOnMs, int PulseOffMs, long ExpectedVersion);

public sealed record PrintStatusDto(bool HasActiveProfile, string? ProfileName, int? Columns);

/// <param name="PhysicallyDeleted">Verdadero si el perfil se borró; falso si tenía historial de impresión y solo se retiró.</param>
public sealed record DeletePrintProfileResultDto(bool PhysicallyDeleted);

public sealed record PrintJobDto(Guid Id, Guid SaleId, string CopyType, int CopyNumber,
    string Status, int Attempts, DateTimeOffset RequestedAtUtc,
    DateTimeOffset? QueueAcceptedAtUtc, string? Error);

public sealed record PrintJobsQuery(int Page = 1, int PageSize = 20);

/// <summary>
/// Resultado de mandar el comprobante de una corrección a la impresora de tickets (pruebas del usuario del 26/11/2026):
/// IMPRESO (Windows lo aceptó), FALLIDO, INCIERTO o SIN_PERFIL (no hay impresora de tickets; se imprime en el navegador).
/// </summary>
public sealed record CorrectionPrintResultDto(string Status, string? Message);

/// <summary>El comprobante preparado y cómo le fue al mandarlo a la impresora de tickets.</summary>
public sealed record SaleCorrectionPrintDto(SaleCorrectionReceiptDto Receipt, string Status, string? Message);

public interface IReceiptPrintingService
{
    Task<Result<PrintProfileDto>> CreateProfileAsync(ActorContext actor,
        CreatePrintProfileRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyCollection<PrintProfileDto>>> ListProfilesAsync(ActorContext actor,
        CancellationToken ct = default);
    Task<Result<PrintProfileDto>> UpdateProfileDrawerAsync(ActorContext actor, Guid profileId,
        UpdatePrintProfileDrawerRequest request, CancellationToken ct = default);
    Task<Result<DeletePrintProfileResultDto>> DeleteProfileAsync(ActorContext actor, Guid profileId,
        CancellationToken ct = default);
    Task<Result<PrintStatusDto>> GetStatusAsync(ActorContext actor, CancellationToken ct = default);
    Task<Result<PagedResult<PrintJobDto>>> ListJobsAsync(ActorContext actor,
        PrintJobsQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PrintJobDto>>> ListSaleJobsAsync(ActorContext actor,
        Guid saleId, PrintJobsQuery query, CancellationToken ct = default);
    Task<Result<PrintJobDto>> RetryAsync(ActorContext actor, Guid jobId,
        CancellationToken ct = default);
    Task<Result<PrintJobDto>> RetrySaleJobAsync(ActorContext actor, Guid saleId, Guid jobId,
        CancellationToken ct = default);
    Task<PrintJobDto?> QueueOriginalAsync(Guid saleId, CancellationToken ct = default);
    Task<PrintJobDto?> QueueReprintAsync(Guid requestId, Guid saleId, Guid actorId,
        CancellationToken ct = default);
    Task<bool> RecoverOneOriginalAsync(CancellationToken ct = default);
    Task<bool> ProcessOneAsync(CancellationToken ct = default);
    /// <summary>Manda el comprobante de una corrección a la impresora de tickets predeterminada, sin guardar un trabajo.</summary>
    Task<CorrectionPrintResultDto> PrintCorrectionAsync(Guid actorId, SaleCorrectionReceiptDto receipt,
        CancellationToken ct = default);
}

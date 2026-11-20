using System.Data;
using System.Text.Json;
using ControlPlus.Application.Common;
using ControlPlus.Application.Sales.Contracts;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Sales.Services;

public sealed partial class SalesService
{
    public async Task<Result<SaleCorrectionReceiptDto>> PrepareCorrectionReceiptAsync(ActorContext actor,
        Guid saleId, Guid changeId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var key = idempotencyKey?.Trim() ?? "";
        if (saleId == Guid.Empty || changeId == Guid.Empty || key.Length is < 1 or > 100 ||
            key.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.' or ':')))
            return Result.Failure<SaleCorrectionReceiptDto>(Validation("El comprobante o la clave de idempotencia es inválido."));
        await using var transaction = await transactionManager.BeginAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var installation = await repository.GetActiveInstallationForUpdateAsync(cancellationToken);
        if (installation is null) return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.NotFound("la instalación activa"));
        var snapshot = await userRepository.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        if (snapshot is null) return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.Unauthorized());
        if (!snapshot.PermissionCodes.Contains(PermissionCodes.SalesReprint, StringComparer.OrdinalIgnoreCase))
            return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.Forbidden());
        var sale = await repository.GetSaleAsync(saleId, cancellationToken);
        if (sale is null || sale.InstalacionId != installation.Id)
            return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.NotFound("la venta"));
        if (!CanReadSale(snapshot.PermissionCodes, actor.UserId, sale))
            return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.Forbidden());
        var fingerprint = Hash($"CORRECTION-RECEIPT:{actor.UserId:N}:{saleId:N}:{changeId:N}");
        var previous = await repository.GetIdempotencyAsync(installation.Id, key, cancellationToken);
        if (previous is not null)
        {
            if (previous.TipoOperacion != "COMPROBANTE_CAMBIO" || previous.UsuarioId != actor.UserId ||
                previous.EntidadId != changeId || previous.HashSolicitud != fingerprint || previous.Resultado is null)
                return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.Conflict("La clave de idempotencia ya se utilizó con otra solicitud."));
            SaleCorrectionReceiptDto? saved;
            try { saved = JsonSerializer.Deserialize<SaleCorrectionReceiptDto>(previous.Resultado); }
            catch (JsonException) { saved = null; }
            return saved is null ? Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.Conflict("El comprobante anterior no puede recuperarse."))
                : Result.Success(saved);
        }
        var change = (await repository.GetChangesAsync(saleId, cancellationToken)).SingleOrDefault(x => x.Id == changeId);
        if (change is null) return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.NotFound("la corrección"));
        if (change.Estado != "CONFIRMADO")
            return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.Conflict("La corrección no está confirmada."));
        var terminal = await repository.GetActiveTerminalAsync(installation.Id, cancellationToken);
        var establishment = await repository.GetActiveEstablishmentAsync(cancellationToken);
        if (terminal is null || establishment is null)
            return Result.Failure<SaleCorrectionReceiptDto>(ApplicationError.NotFound("la configuración de la instalación"));
        var now = clock.UtcNow;
        var response = new SaleCorrectionReceiptDto(Guid.CreateVersion7(), sale.Id, VisibleNumber(sale), now,
            MapChange(change), establishment.NombreComercial);
        await repository.AddIdempotencyAsync(new OperacionIdempotente
        {
            Id = response.RequestId, InstalacionId = installation.Id, TerminalId = terminal.Id,
            UsuarioId = actor.UserId, ClaveIdempotencia = key, TipoOperacion = "COMPROBANTE_CAMBIO",
            HashSolicitud = fingerprint, Entidad = nameof(CambioVenta), EntidadId = changeId,
            Resultado = JsonSerializer.Serialize(response), CorrelacionId = Guid.CreateVersion7(), FechaRegistro = now.UtcDateTime
        }, cancellationToken);
        await AuditWriter.WriteAsync(auditRepository, clock, actor.UserId, AuditAction.SaleReprintRequested,
            nameof(CambioVenta), changeId, new { response.RequestId, SaleId = saleId, ChangeId = changeId,
                DocumentType = "CORRECCION_VENTA", Status = "PREPARADO_NAVEGADOR" }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result.Success(response);
    }
}

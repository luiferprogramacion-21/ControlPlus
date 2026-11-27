using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Backups;

/// <summary>
/// Respaldos de Fase 4: volcado lógico (pg_dump) cada 30 minutos, al cerrar caja y a pedido del Administrador; copia
/// a un disco físicamente separado y copia externa cifrada; retención diaria, semanal y mensual; todo registrado en
/// configuracion.ejecucion_respaldo y copia_respaldo. Los errores guardados nunca contienen rutas internas ni secretos.
/// </summary>
public sealed class BackupService(
    IBackupRepository repository,
    IBackupEngine engine,
    IBackupRuntime runtime,
    IBackupRequestQueue queue,
    IUserRepository users,
    IAuditRepository audits,
    IUnitOfWork unitOfWork,
    IClock clock) : IBackupService
{
    // Un solo respaldo a la vez en este equipo; un pedido que llega durante otro espera y respalda lo más reciente.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const int StagingFilesToKeep = 3;
    private const int RecentEvents = 8;

    public async Task<BackupExecutionDto?> RunAsync(string trigger, CancellationToken cancellationToken = default)
    {
        var settings = runtime.Settings;
        if (!settings.Enabled) return null;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
            if (installationId is null) return null;
            var started = clock.UtcNow;
            var execution = new EjecucionRespaldo
            {
                Id = Guid.CreateVersion7(), InstalacionId = installationId.Value, FechaInicio = started.UtcDateTime,
                Estrategia = BackupCodes.LogicalStrategy, Disparador = trigger, Resultado = BackupCodes.Running
            };
            await repository.AddExecutionAsync(execution, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            DumpResult dump;
            try
            {
                dump = await engine.DumpAsync($"controlplus-{started.UtcDateTime:yyyyMMddTHHmmssZ}-{trigger}.dump", cancellationToken);
                if (!dump.Verified) throw new BackupStepException("El respaldo generado no pasó la verificación de pg_restore.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Finish(execution, BackupCodes.Failed, Sanitize(exception, "No se pudo generar el respaldo."));
                await repository.SaveChangesAsync(cancellationToken);
                return Map(execution);
            }

            execution.TamanoBytes = dump.SizeBytes;
            execution.HashIntegridad = dump.Sha256;
            execution.RutaArtefactoLocal = dump.StagingPath;
            Finish(execution, BackupCodes.Succeeded, null);
            await repository.SaveChangesAsync(cancellationToken);

            foreach (var destination in (await repository.GetDestinationsAsync(installationId.Value, cancellationToken))
                         .Where(x => x.Activo))
                await CopyAsync(execution, destination, dump, settings, cancellationToken);

            engine.PruneStaging(StagingFilesToKeep);
            return Map(execution);
        }
        finally { Gate.Release(); }
    }

    public async Task<bool> IsDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var settings = runtime.Settings;
        if (!settings.Enabled) return false;
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return false;
        var last = await repository.GetLastExecutionAsync(installationId.Value, true, cancellationToken);
        return last is null || now.UtcDateTime - DateTime.SpecifyKind(last.FechaInicio, DateTimeKind.Utc) >=
            TimeSpan.FromMinutes(settings.IntervalMinutes);
    }

    public async Task<Result<BackupStatusDto>> GetStatusAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<BackupStatusDto>(error);
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<BackupStatusDto>(ApplicationError.NotFound("la instalación activa"));
        var settings = runtime.Settings;

        var configured = await repository.GetDestinationsAsync(installationId.Value, cancellationToken);
        var destinations = new List<BackupDestinationDto>();
        BackupCopyDto? lastExternal = null;
        foreach (var type in BackupCodes.DestinationTypes)
        {
            var destination = configured.FirstOrDefault(x => x.TipoDestino == type);
            if (destination is null)
            {
                destinations.Add(new BackupDestinationDto(type, DefaultName(type), null, false,
                    type == BackupCodes.ExternalDestination, settings.PassphraseConfigured, 7, 4, 12, null, null, null, null));
                continue;
            }
            var lastCopy = await repository.GetLastCopyAsync(destination.Id, cancellationToken);
            var copyDto = lastCopy is null ? null : MapCopy(lastCopy, type);
            if (type == BackupCodes.ExternalDestination) lastExternal = copyDto;
            var (free, total) = engine.GetSpace(destination.UbicacionBase);
            destinations.Add(new BackupDestinationDto(type, destination.Nombre, destination.UbicacionBase, destination.Activo,
                destination.RequiereCifrado, settings.PassphraseConfigured, destination.RetencionDiaria,
                destination.RetencionSemanal, destination.RetencionMensual, free, total, copyDto, destination.Version));
        }

        var executions = await repository.GetRecentExecutionsAsync(installationId.Value, RecentEvents, cancellationToken);
        var copies = await repository.GetRecentCopiesAsync(installationId.Value, RecentEvents, cancellationToken);
        var events = executions.Select(x => new BackupEventDto(Utc(x.FechaInicio), "RESPALDO", TriggerText(x.Disparador), x.Resultado))
            .Concat(copies.Select(x => new BackupEventDto(Utc(x.Copy.FechaUltimoIntento ?? x.Copy.FechaCompletada ?? DateTime.UtcNow),
                "COPIA", x.DestinationType == BackupCodes.ExternalDestination ? "Envío de copia externa" : "Copia en disco local",
                x.Copy.Estado)))
            .OrderByDescending(x => x.At).Take(RecentEvents).ToArray();

        var last = await repository.GetLastExecutionAsync(installationId.Value, false, cancellationToken);
        var lastSuccessful = await repository.GetLastExecutionAsync(installationId.Value, true, cancellationToken);
        return Result.Success(new BackupStatusDto(settings.Enabled, settings.IntervalMinutes,
            last is null ? null : Map(last), lastSuccessful is null ? null : Map(lastSuccessful), lastExternal,
            destinations, events));
    }

    public async Task<Result<BackupRunRequested>> RequestManualAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<BackupRunRequested>(error);
        if (!runtime.Settings.Enabled)
            return Result.Failure<BackupRunRequested>(ApplicationError.Conflict(
                "El servicio de respaldos no está activado en este equipo."));
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<BackupRunRequested>(ApplicationError.NotFound("la instalación activa"));
        if (!queue.Request(BackupCodes.Manual))
            return Result.Failure<BackupRunRequested>(ApplicationError.Conflict("Ya hay un respaldo pendiente; espere a que termine."));
        var requestId = Guid.CreateVersion7();
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.BackupRequested, nameof(EjecucionRespaldo),
            requestId, new { Trigger = BackupCodes.Manual }, requestId, null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new BackupRunRequested(requestId, BackupCodes.Manual));
    }

    public async Task<Result<BackupDestinationDto>> UpdateDestinationAsync(ActorContext actor, string type,
        UpdateBackupDestinationRequest request, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<BackupDestinationDto>(error);
        type = (type ?? string.Empty).Trim().ToUpperInvariant();
        if (!BackupCodes.DestinationTypes.Contains(type))
            return Result.Failure<BackupDestinationDto>(ApplicationError.Validation(
                "El destino debe ser LOCAL_FISICO_SEPARADO o EXTERNO_CIFRADO."));
        var path = request?.Path?.Trim() ?? string.Empty;
        if (path.Length is 0 or > 400)
            return Result.Failure<BackupDestinationDto>(ApplicationError.Validation("Indique la carpeta del destino (hasta 400 caracteres)."));
        if (request!.Active)
        {
            var invalid = engine.ValidateDestination(type, path);
            if (invalid is not null) return Result.Failure<BackupDestinationDto>(ApplicationError.Validation(invalid));
            if (type == BackupCodes.ExternalDestination && !runtime.Settings.PassphraseConfigured)
                return Result.Failure<BackupDestinationDto>(ApplicationError.Conflict(
                    "Configure primero la contraseña de cifrado en este equipo (Backup:EncryptionPassphrase)."));
        }

        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<BackupDestinationDto>(ApplicationError.NotFound("la instalación activa"));
        var now = clock.UtcNow.UtcDateTime;
        var destination = await repository.GetDestinationForUpdateAsync(installationId.Value, type, cancellationToken);
        object? previous = null;
        if (destination is null)
        {
            destination = new DestinoRespaldo
            {
                Id = Guid.CreateVersion7(), InstalacionId = installationId.Value, Nombre = DefaultName(type),
                TipoDestino = type, UbicacionBase = path, RequiereCifrado = type == BackupCodes.ExternalDestination,
                ReferenciaSecreto = type == BackupCodes.ExternalDestination ? BackupCodes.PassphraseReference : null,
                RetencionDiaria = 7, RetencionSemanal = 4, RetencionMensual = 12, ConservarPreActualizacion = true,
                Activo = request.Active, FechaCreacion = now, Version = 1
            };
            await repository.AddDestinationAsync(destination, cancellationToken);
        }
        else
        {
            if (request.ExpectedVersion != destination.Version)
                return Result.Failure<BackupDestinationDto>(ApplicationError.ConcurrencyConflict("El destino fue modificado."));
            previous = new { destination.UbicacionBase, destination.Activo };
            destination.UbicacionBase = path;
            destination.Activo = request.Active;
            destination.FechaModificacion = now;
        }
        // La auditoría guarda el tipo y si está activo; la ruta queda solo en la configuración del destino.
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.BackupDestinationUpdated,
            nameof(DestinoRespaldo), destination.Id,
            new { Type = type, request.Active, PathChanged = previous is null || !Equals(previous, new { destination.UbicacionBase, destination.Activo }) },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var (free, total) = engine.GetSpace(destination.UbicacionBase);
        return Result.Success(new BackupDestinationDto(type, destination.Nombre, destination.UbicacionBase, destination.Activo,
            destination.RequiereCifrado, runtime.Settings.PassphraseConfigured, destination.RetencionDiaria,
            destination.RetencionSemanal, destination.RetencionMensual, free, total, null, destination.Version));
    }

    public async Task<Result<RestoreOverviewDto>> GetRestoreOverviewAsync(ActorContext actor, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<RestoreOverviewDto>(error);
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<RestoreOverviewDto>(ApplicationError.NotFound("la instalación activa"));
        var establishment = await repository.GetEstablishmentAsync(installationId.Value, cancellationToken);
        if (establishment is null) return Result.Failure<RestoreOverviewDto>(ApplicationError.NotFound("el establecimiento activo"));

        var copies = (await repository.GetCompletedCopiesAsync(installationId.Value, 30, cancellationToken))
            .Select(x => new RestorableCopyDto(x.Copy.Id, Utc(x.Execution.FechaInicio), x.DestinationType, x.Execution.TamanoBytes,
                x.Copy.HashVerificado, x.Copy.UbicacionFinal is not null && engine.CopyExists(x.Copy.UbicacionFinal)))
            .ToArray();
        var last = await repository.GetLastRestoreTestAsync(installationId.Value, cancellationToken);
        // Fase 4: probar al menos cada dias_prueba_restauracion días; sin una prueba exitosa, ya está vencida.
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var nextDue = last is { Resultado: BackupCodes.TestSucceeded }
            ? DateOnly.FromDateTime(last.FechaInicio).AddDays(establishment.DiasPruebaRestauracion)
            : today;
        return Result.Success(new RestoreOverviewDto(establishment.RpoMinutos, establishment.RtoMinutos,
            establishment.DiasPruebaRestauracion, last is null ? null : MapTest(last), nextDue, copies, establishment.Version));
    }

    /// <summary>
    /// Cambia las metas de respaldo y recuperación (decisión del usuario del 27/11/2026: 30 minutos de pérdida, 30 de
    /// recuperación y prueba cada mes). Solo el Administrador con BACKUPS.MANAGE; se audita con los valores anteriores.
    /// </summary>
    public async Task<Result<RestoreOverviewDto>> UpdateRestoreGoalsAsync(ActorContext actor, UpdateRestoreGoalsRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<RestoreOverviewDto>(error);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RpoMinutes is < 5 or > 1440)
            return Result.Failure<RestoreOverviewDto>(ApplicationError.Validation("La pérdida máxima va de 5 a 1.440 minutos (un día)."));
        if (request.RtoMinutes is < 5 or > 1440)
            return Result.Failure<RestoreOverviewDto>(ApplicationError.Validation("El tiempo para recuperar va de 5 a 1.440 minutos (un día)."));
        if (request.RestoreTestDays is < 1 or > 365)
            return Result.Failure<RestoreOverviewDto>(ApplicationError.Validation("La prueba de restauración se hace cada 1 a 365 días."));
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<RestoreOverviewDto>(ApplicationError.NotFound("la instalación activa"));
        var establishment = await repository.GetEstablishmentForUpdateAsync(installationId.Value, cancellationToken);
        if (establishment is null) return Result.Failure<RestoreOverviewDto>(ApplicationError.NotFound("el establecimiento activo"));
        if (request.ExpectedVersion != establishment.Version)
            return Result.Failure<RestoreOverviewDto>(ApplicationError.ConcurrencyConflict("Las metas fueron cambiadas por otra persona."));
        var previous = new { establishment.RpoMinutos, establishment.RtoMinutos, establishment.DiasPruebaRestauracion };
        establishment.RpoMinutos = request.RpoMinutes;
        establishment.RtoMinutos = request.RtoMinutes;
        establishment.DiasPruebaRestauracion = request.RestoreTestDays;
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.BackupGoalsUpdated,
            nameof(Establecimiento), establishment.Id,
            new { Before = previous, After = new { RpoMinutos = request.RpoMinutes, RtoMinutos = request.RtoMinutes, DiasPruebaRestauracion = request.RestoreTestDays } },
            Guid.CreateVersion7(), null, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetRestoreOverviewAsync(actor, cancellationToken);
    }

    public async Task<Result<RestoreTestDto>> TestRestoreAsync(ActorContext actor, Guid copyId, CancellationToken cancellationToken = default)
    {
        var error = await RequireAdministratorAsync(actor, cancellationToken);
        if (error is not null) return Result.Failure<RestoreTestDto>(error);
        var settings = runtime.Settings;
        if (!settings.Enabled)
            return Result.Failure<RestoreTestDto>(ApplicationError.Conflict("El servicio de respaldos no está activado en este equipo."));
        var installationId = await repository.GetActiveInstallationIdAsync(cancellationToken);
        if (installationId is null) return Result.Failure<RestoreTestDto>(ApplicationError.NotFound("la instalación activa"));
        var found = await repository.GetCopyAsync(installationId.Value, copyId, cancellationToken);
        if (found is null) return Result.Failure<RestoreTestDto>(ApplicationError.NotFound("la copia de respaldo"));
        var (copy, execution, _) = found.Value;
        if (copy.Estado != BackupCodes.CopyCompleted || copy.UbicacionFinal is null)
            return Result.Failure<RestoreTestDto>(ApplicationError.Conflict("Solo se puede probar una copia completada."));
        if (!engine.CopyExists(copy.UbicacionFinal))
            return Result.Failure<RestoreTestDto>(ApplicationError.Conflict(
                "El archivo de la copia ya no está disponible (depurado por retención o disco desconectado)."));
        if (copy.Cifrado && !settings.PassphraseConfigured)
            return Result.Failure<RestoreTestDto>(ApplicationError.Conflict("Falta la contraseña de cifrado en este equipo."));
        var permissionId = await repository.GetPermissionIdAsync(PermissionCodes.BackupsManage, cancellationToken);
        if (permissionId is null)
            return Result.Failure<RestoreTestDto>(ApplicationError.Conflict("El permiso de respaldos no está activo."));

        var now = clock.UtcNow.UtcDateTime;
        var correlationId = Guid.CreateVersion7();
        // El Administrador autoriza su propia prueba: no cambia datos de la operación y queda registrada.
        var authorization = new AutorizacionOperacion
        {
            Id = Guid.CreateVersion7(), UsuarioSolicitanteId = actor.UserId, UsuarioAutorizadorId = actor.UserId,
            PermisoId = permissionId.Value, TipoOperacion = BackupCodes.RestoreTestType, CorrelacionId = correlationId,
            DescripcionOperacion = "Prueba de restauración de un respaldo en una base temporal, por el Administrador.",
            MetodoAutenticacion = "PASSWORD", CredencialAutorizadorId = null, Resultado = "EXITOSA", Estado = "UTILIZADA",
            FechaSolicitud = now, FechaExpiracion = now.AddMinutes(5), FechaUtilizacion = now
        };
        var test = new PruebaRestauracion
        {
            Id = Guid.CreateVersion7(), CopiaRespaldoId = copy.Id, EjecutadaPorId = actor.UserId,
            AutorizacionOperacionId = authorization.Id, FechaInicio = now, Resultado = BackupCodes.TestRunning
        };
        await repository.AddAuthorizationAsync(authorization, cancellationToken);
        await repository.AddRestoreTestAsync(test, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        RestoreTestResult result;
        try { result = await engine.TestRestoreAsync(copy.UbicacionFinal, copy.Cifrado, execution.HashIntegridad, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new RestoreTestResult(false, false, TimeSpan.Zero, null, Sanitize(exception, "No se pudo completar la prueba de restauración."));
        }

        var previous = await repository.GetPreviousSuccessfulAsync(installationId.Value, execution.FechaInicio, cancellationToken);
        test.FechaFin = clock.UtcNow.UtcDateTime;
        test.IntegridadVerificada = result.IntegrityVerified;
        test.AplicacionVerificada = result.ApplicationVerified;
        test.Resultado = result.IntegrityVerified && result.ApplicationVerified ? BackupCodes.TestSucceeded : BackupCodes.TestFailed;
        // RTO observado: minutos que tomó recuperar. RPO observado: separación con el respaldo exitoso anterior.
        test.RtoObservadoMinutos = (int)Math.Ceiling(result.Duration.TotalMinutes);
        test.RpoObservadoMinutos = previous is null ? null : (int)Math.Ceiling((execution.FechaInicio - previous.FechaInicio).TotalMinutes);
        test.Observaciones = result.AppliedMigrations is int migrations ? $"Migraciones restauradas: {migrations}." : null;
        test.ErrorSanitizado = result.Error;
        await AuditWriter.WriteCashAsync(audits, clock, actor.UserId, AuditAction.BackupRestoreTested, nameof(PruebaRestauracion),
            test.Id, new { CopyId = copy.Id, test.Resultado, test.IntegridadVerificada, test.AplicacionVerificada },
            correlationId, actor.UserId, null, null, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(MapTest(test));
    }

    private static RestoreTestDto MapTest(PruebaRestauracion x) => new(x.Id, x.CopiaRespaldoId, Utc(x.FechaInicio),
        x.FechaFin is null ? null : Utc(x.FechaFin.Value), x.Resultado, x.RpoObservadoMinutos, x.RtoObservadoMinutos,
        x.IntegridadVerificada, x.AplicacionVerificada, x.ErrorSanitizado);

    private async Task CopyAsync(EjecucionRespaldo execution, DestinoRespaldo destination, DumpResult dump,
        BackupRuntimeSettings settings, CancellationToken cancellationToken)
    {
        var copy = new CopiaRespaldo
        {
            Id = Guid.CreateVersion7(), EjecucionRespaldoId = execution.Id, DestinoRespaldoId = destination.Id,
            Estado = BackupCodes.CopySending, Intentos = 1, Cifrado = destination.RequiereCifrado,
            FechaUltimoIntento = clock.UtcNow.UtcDateTime
        };
        await repository.AddCopyAsync(copy, cancellationToken);
        try
        {
            if (destination.RequiereCifrado && !settings.PassphraseConfigured)
                throw new BackupStepException("Falta la contraseña de cifrado en este equipo.");
            var result = await engine.CopyAsync(dump, destination.UbicacionBase, destination.RequiereCifrado, cancellationToken);
            if (!result.Verified) throw new BackupStepException("La copia escrita no coincide con el respaldo original.");
            copy.Estado = BackupCodes.CopyCompleted;
            copy.UbicacionFinal = result.FinalPath;
            copy.HashVerificado = true;
            copy.FechaCompletada = clock.UtcNow.UtcDateTime;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            copy.Estado = BackupCodes.CopyFailed;
            copy.ErrorSanitizado = Sanitize(exception, "No se pudo escribir la copia en el destino.");
        }
        await repository.SaveChangesAsync(cancellationToken);

        if (copy.Estado != BackupCodes.CopyCompleted) return;
        try
        {
            var rules = new RetentionRules(destination.RetencionDiaria, destination.RetencionSemanal,
                destination.RetencionMensual, destination.ConservarPreActualizacion);
            foreach (var old in BackupRetentionPolicy.SelectForDeletion(engine.ListCopies(destination.UbicacionBase), rules,
                         clock.UtcNow.UtcDateTime))
                engine.Delete(destination.UbicacionBase, old.FileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Si no se puede depurar, la copia nueva sigue siendo válida; se reintenta en el próximo respaldo.
        }
    }

    private async Task<ApplicationError?> RequireAdministratorAsync(ActorContext actor, CancellationToken cancellationToken)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, cancellationToken);
        return snapshot is null ? ApplicationError.Unauthorized()
            : snapshot.HighestRoleLevel != RoleLevel.Administrador ||
              !snapshot.PermissionCodes.Contains(PermissionCodes.BackupsManage, StringComparer.OrdinalIgnoreCase)
                ? ApplicationError.Forbidden()
                : null;
    }

    private void Finish(EjecucionRespaldo execution, string result, string? error)
    {
        execution.Resultado = result;
        execution.FechaFin = clock.UtcNow.UtcDateTime;
        execution.ErrorSanitizado = error;
    }

    // Solo los mensajes propios del servicio llegan a la base; los técnicos se reemplazan por uno genérico.
    private static string Sanitize(Exception exception, string fallback) =>
        exception is BackupStepException ? exception.Message : fallback;

    private static string DefaultName(string type) =>
        type == BackupCodes.ExternalDestination ? "Copia externa cifrada" : "Copia local separada";

    private static string TriggerText(string trigger) => trigger switch
    {
        BackupCodes.Scheduled => "Respaldo automático",
        BackupCodes.CashClose => "Respaldo al cerrar caja",
        BackupCodes.Manual => "Respaldo manual",
        BackupCodes.PreUpdate => "Respaldo antes de actualizar",
        _ => "Respaldo"
    };

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static BackupExecutionDto Map(EjecucionRespaldo x) => new(x.Id, Utc(x.FechaInicio),
        x.FechaFin is null ? null : Utc(x.FechaFin.Value), x.Disparador, x.Resultado, x.TamanoBytes,
        x.Resultado == BackupCodes.Succeeded && x.HashIntegridad is not null, x.ErrorSanitizado);

    private static BackupCopyDto MapCopy(CopiaRespaldo x, string type) => new(x.Id, x.EjecucionRespaldoId, type,
        x.FechaCompletada is null ? null : Utc(x.FechaCompletada.Value), x.Estado, x.Cifrado, x.HashVerificado, x.ErrorSanitizado);
}

/// <summary>Falla de un paso del respaldo con un mensaje apto para guardar y mostrar.</summary>
public sealed class BackupStepException(string message) : Exception(message);

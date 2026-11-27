using ControlPlus.Application.Backups;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfBackupRepository(OfficialControlPlusDbContext dbContext) : IBackupRepository
{
    public Task<Guid?> GetActiveInstallationIdAsync(CancellationToken cancellationToken) =>
        dbContext.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<DestinoRespaldo>> GetDestinationsAsync(Guid installationId, CancellationToken cancellationToken) =>
        await dbContext.DestinoRespaldo.AsNoTracking().Where(x => x.InstalacionId == installationId)
            .OrderBy(x => x.TipoDestino).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public Task<DestinoRespaldo?> GetDestinationForUpdateAsync(Guid installationId, string type, CancellationToken cancellationToken) =>
        dbContext.DestinoRespaldo.Where(x => x.InstalacionId == installationId && x.TipoDestino == type)
            .OrderBy(x => x.FechaCreacion).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task AddDestinationAsync(DestinoRespaldo destination, CancellationToken cancellationToken) =>
        await dbContext.DestinoRespaldo.AddAsync(destination, cancellationToken);

    public async Task AddExecutionAsync(EjecucionRespaldo execution, CancellationToken cancellationToken) =>
        await dbContext.EjecucionRespaldo.AddAsync(execution, cancellationToken);

    public async Task AddCopyAsync(CopiaRespaldo copy, CancellationToken cancellationToken) =>
        await dbContext.CopiaRespaldo.AddAsync(copy, cancellationToken);

    public Task<EjecucionRespaldo?> GetLastExecutionAsync(Guid installationId, bool onlySuccessful, CancellationToken cancellationToken) =>
        dbContext.EjecucionRespaldo.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && (!onlySuccessful || x.Resultado == BackupCodes.Succeeded))
            .OrderByDescending(x => x.FechaInicio).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<CopiaRespaldo?> GetLastCopyAsync(Guid destinationId, CancellationToken cancellationToken) =>
        dbContext.CopiaRespaldo.AsNoTracking().Where(x => x.DestinoRespaldoId == destinationId)
            .OrderByDescending(x => x.FechaUltimoIntento).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EjecucionRespaldo>> GetRecentExecutionsAsync(Guid installationId, int limit,
        CancellationToken cancellationToken) =>
        await dbContext.EjecucionRespaldo.AsNoTracking().Where(x => x.InstalacionId == installationId)
            .OrderByDescending(x => x.FechaInicio).ThenByDescending(x => x.Id).Take(limit).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(CopiaRespaldo Copy, string DestinationType)>> GetRecentCopiesAsync(Guid installationId,
        int limit, CancellationToken cancellationToken)
    {
        var rows = await dbContext.CopiaRespaldo.AsNoTracking()
            .Where(x => x.DestinoRespaldo.InstalacionId == installationId)
            .OrderByDescending(x => x.FechaUltimoIntento).ThenByDescending(x => x.Id).Take(limit)
            .Select(x => new { Copy = x, x.DestinoRespaldo.TipoDestino }).ToListAsync(cancellationToken);
        return rows.Select(x => (x.Copy, x.TipoDestino)).ToArray();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public Task<Establecimiento?> GetEstablishmentAsync(Guid installationId, CancellationToken cancellationToken) =>
        dbContext.Instalacion.AsNoTracking().Where(x => x.Id == installationId)
            .Select(x => x.Establecimiento).FirstOrDefaultAsync(cancellationToken);

    public Task<Establecimiento?> GetEstablishmentForUpdateAsync(Guid installationId, CancellationToken cancellationToken) =>
        dbContext.Establecimiento.Where(x => x.Instalacion.Any(i => i.Id == installationId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<(CopiaRespaldo Copy, EjecucionRespaldo Execution, string DestinationType)>> GetCompletedCopiesAsync(
        Guid installationId, int limit, CancellationToken cancellationToken)
    {
        var rows = await dbContext.CopiaRespaldo.AsNoTracking()
            .Where(x => x.Estado == BackupCodes.CopyCompleted && x.DestinoRespaldo.InstalacionId == installationId)
            .OrderByDescending(x => x.EjecucionRespaldo.FechaInicio).ThenByDescending(x => x.Id).Take(limit)
            .Select(x => new { Copy = x, Execution = x.EjecucionRespaldo, x.DestinoRespaldo.TipoDestino })
            .ToListAsync(cancellationToken);
        return rows.Select(x => (x.Copy, x.Execution, x.TipoDestino)).ToArray();
    }

    public async Task<(CopiaRespaldo Copy, EjecucionRespaldo Execution, string DestinationType)?> GetCopyAsync(Guid installationId,
        Guid copyId, CancellationToken cancellationToken)
    {
        var row = await dbContext.CopiaRespaldo.AsNoTracking()
            .Where(x => x.Id == copyId && x.DestinoRespaldo.InstalacionId == installationId)
            .Select(x => new { Copy = x, Execution = x.EjecucionRespaldo, x.DestinoRespaldo.TipoDestino })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.Copy, row.Execution, row.TipoDestino);
    }

    public Task<EjecucionRespaldo?> GetPreviousSuccessfulAsync(Guid installationId, DateTime before, CancellationToken cancellationToken) =>
        dbContext.EjecucionRespaldo.AsNoTracking()
            .Where(x => x.InstalacionId == installationId && x.Resultado == BackupCodes.Succeeded && x.FechaInicio < before)
            .OrderByDescending(x => x.FechaInicio).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<PruebaRestauracion?> GetLastRestoreTestAsync(Guid installationId, CancellationToken cancellationToken) =>
        dbContext.PruebaRestauracion.AsNoTracking()
            .Where(x => x.CopiaRespaldo.DestinoRespaldo.InstalacionId == installationId)
            .OrderByDescending(x => x.FechaInicio).ThenByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task AddRestoreTestAsync(PruebaRestauracion test, CancellationToken cancellationToken) =>
        await dbContext.PruebaRestauracion.AddAsync(test, cancellationToken);

    public async Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken cancellationToken) =>
        await dbContext.AutorizacionOperacion.AddAsync(authorization, cancellationToken);

    public Task<Guid?> GetPermissionIdAsync(string code, CancellationToken cancellationToken) =>
        dbContext.Permiso.AsNoTracking().Where(x => x.Codigo == code && x.Activo)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
}

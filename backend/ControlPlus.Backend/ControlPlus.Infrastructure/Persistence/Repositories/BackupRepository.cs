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
}

using ControlPlus.Application.Reports;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfDashboardInstallationRepository(OfficialControlPlusDbContext dbContext) : IDashboardInstallationRepository
{
    public Task<DashboardInstallation?> GetActiveAsync(CancellationToken cancellationToken) =>
        dbContext.Instalacion.AsNoTracking().Where(x => x.Activo)
            .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id)
            .Select(x => new DashboardInstallation(x.Id, x.Establecimiento.ZonaHoraria))
            .FirstOrDefaultAsync(cancellationToken);
}

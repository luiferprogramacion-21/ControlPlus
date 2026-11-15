using ControlPlus.Application.Layaways.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfLayawayCreateRepository(OfficialControlPlusDbContext dbContext) : ILayawayCreateRepository
{
    public Task<Establecimiento?> GetEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken) =>
        dbContext.Establecimiento.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == establishmentId && x.Activo, cancellationToken);

    public async Task<Cliente?> GetActiveCustomerForUpdateAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Cliente.FromSqlInterpolated(
            $"SELECT c.* FROM catalogo.cliente AS c WHERE c.id = {customerId} AND c.activo FOR UPDATE")
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<SesionOperador?> GetActiveOperatorSessionForUpdateAsync(
        Guid shiftId, Guid terminalId, Guid userId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.SesionOperador.FromSqlInterpolated(
            $"""
             SELECT s.* FROM caja.sesion_operador AS s
             WHERE s.turno_caja_id = {shiftId} AND s.terminal_id = {terminalId}
               AND s.usuario_id = {userId} AND s.estado = 'ACTIVA'
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<ConsecutivoDocumento?> GetSequenceForUpdateAsync(
        Guid installationId, string series, string documentType, CancellationToken cancellationToken)
    {
        // Creating a layaway or taking a payment is the first use: the consecutive is created here when it
        // does not exist yet.
        await DocumentSequenceProvisioner.EnsureAsync(
            dbContext, installationId, series, documentType, cancellationToken);
        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated(
            $"""
             SELECT c.* FROM configuracion.consecutivo_documento AS c
             WHERE c.instalacion_id = {installationId} AND c.serie = {series}
               AND c.tipo_documento = {documentType}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task AddAsync(
        Apartado layaway, IReadOnlyCollection<DetalleApartado> lines,
        IReadOnlyCollection<PagoApartado> payments,
        IReadOnlyCollection<MovimientoInventario> inventoryMovements,
        CancellationToken cancellationToken)
    {
        await dbContext.Apartado.AddAsync(layaway, cancellationToken);
        await dbContext.DetalleApartado.AddRangeAsync(lines, cancellationToken);
        await dbContext.PagoApartado.AddRangeAsync(payments, cancellationToken);
        await dbContext.MovimientoInventario.AddRangeAsync(inventoryMovements, cancellationToken);
    }
}

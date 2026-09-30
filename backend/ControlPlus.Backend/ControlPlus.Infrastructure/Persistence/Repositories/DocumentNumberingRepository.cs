using System.Data;
using ControlPlus.Application.Numbering.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfDocumentNumberingTransactionManager(OfficialControlPlusDbContext dbContext)
    : IDocumentNumberingTransactionManager
{
    public async Task<IDocumentNumberingTransaction> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default) =>
        new EfDocumentNumberingTransaction(
            await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken));

    private sealed class EfDocumentNumberingTransaction(IDbContextTransaction transaction)
        : IDocumentNumberingTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

public sealed class EfDocumentNumberingRepository(OfficialControlPlusDbContext dbContext)
    : IDocumentNumberingRepository
{
    public async Task<Instalacion?> GetActiveInstallationAsync(
        bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
        {
            return await dbContext.Instalacion.AsNoTracking()
                .Where(x => x.Activo)
                .OrderBy(x => x.FechaInstalacion).ThenBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rows = await dbContext.Instalacion.FromSqlRaw(
            """
            SELECT i.*
            FROM configuracion.instalacion AS i
            WHERE i.activo
            ORDER BY i.fecha_instalacion, i.id
            LIMIT 1
            FOR UPDATE
            """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<ConsecutivoDocumento?> GetSalesNumberingAsync(
        Guid installationId,
        string series,
        bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
        {
            return await dbContext.ConsecutivoDocumento.AsNoTracking().SingleOrDefaultAsync(
                x => x.InstalacionId == installationId &&
                     x.TipoDocumento == "VENTA" &&
                     x.Serie == series,
                cancellationToken);
        }

        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated(
            $"""
             SELECT c.*
             FROM configuracion.consecutivo_documento AS c
             WHERE c.instalacion_id = {installationId}
               AND c.tipo_documento = 'VENTA'
               AND c.serie = {series}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<bool> HasConfirmedSalesAsync(
        Guid installationId,
        string series,
        CancellationToken cancellationToken = default) =>
        dbContext.Venta.AsNoTracking().AnyAsync(
            x => x.InstalacionId == installationId &&
                 x.Serie == series &&
                 x.Estado == "CONFIRMADA",
            cancellationToken);

    public Task AddAsync(
        ConsecutivoDocumento numbering,
        CancellationToken cancellationToken = default) =>
        dbContext.ConsecutivoDocumento.AddAsync(numbering, cancellationToken).AsTask();
}

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
    public Task<ConsecutivoDocumento?> GetSaleChangeNumberingAsync(
        Guid installationId, string series, bool forUpdate,
        CancellationToken cancellationToken = default) =>
        GetLayawaySequenceAsync(installationId, series, "CAMBIO_VENTA", forUpdate, cancellationToken);

    public Task EnsureNumberingAsync(
        Guid installationId, string series, string documentType,
        CancellationToken cancellationToken = default) =>
        DocumentSequenceProvisioner.EnsureAsync(
            dbContext, installationId, series, documentType, cancellationToken);

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

    public async Task<ConsecutivoDocumento?> GetCreditPaymentNumberingAsync(
        Guid installationId, string series, bool forUpdate, CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
            return await dbContext.ConsecutivoDocumento.AsNoTracking().SingleOrDefaultAsync(
                x => x.InstalacionId == installationId && x.TipoDocumento == "PAGO_CREDITO" && x.Serie == series,
                cancellationToken);
        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated(
            $"""
             SELECT c.* FROM configuracion.consecutivo_documento AS c
             WHERE c.instalacion_id = {installationId} AND c.tipo_documento = 'PAGO_CREDITO' AND c.serie = {series}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<bool> HasCreditPaymentsAsync(Guid installationId, string series,
        CancellationToken cancellationToken = default) =>
        dbContext.PagoCredito.AsNoTracking().AnyAsync(
            x => x.InstalacionId == installationId && x.Serie == series, cancellationToken);

    public Task<ConsecutivoDocumento?> GetLayawayNumberingAsync(
        Guid installationId, string series, bool forUpdate,
        CancellationToken cancellationToken = default) =>
        GetLayawaySequenceAsync(installationId, series, "APARTADO", forUpdate, cancellationToken);

    public Task<ConsecutivoDocumento?> GetLayawayPaymentNumberingAsync(
        Guid installationId, string series, bool forUpdate,
        CancellationToken cancellationToken = default) =>
        GetLayawaySequenceAsync(installationId, series, "PAGO_APARTADO", forUpdate, cancellationToken);

    private async Task<ConsecutivoDocumento?> GetLayawaySequenceAsync(
        Guid installationId, string series, string documentType, bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!forUpdate)
            return await dbContext.ConsecutivoDocumento.AsNoTracking().SingleOrDefaultAsync(
                x => x.InstalacionId == installationId &&
                     x.TipoDocumento == documentType && x.Serie == series,
                cancellationToken);

        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated(
            $"""
             SELECT c.*
             FROM configuracion.consecutivo_documento AS c
             WHERE c.instalacion_id = {installationId}
               AND c.tipo_documento = {documentType}
               AND c.serie = {series}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<ConsecutivoDocumento?> GetPurchaseNumberingAsync(
        Guid installationId, string series, bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
            return await dbContext.ConsecutivoDocumento.AsNoTracking().SingleOrDefaultAsync(
                x => x.InstalacionId == installationId &&
                     x.TipoDocumento == "COMPRA" && x.Serie == series,
                cancellationToken);

        var rows = await dbContext.ConsecutivoDocumento.FromSqlInterpolated(
            $"""
             SELECT c.*
             FROM configuracion.consecutivo_documento AS c
             WHERE c.instalacion_id = {installationId}
               AND c.tipo_documento = 'COMPRA'
               AND c.serie = {series}
             FOR UPDATE
             """).AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<bool> HasNumberedPurchasesAsync(
        Guid installationId, string series,
        CancellationToken cancellationToken = default) =>
        dbContext.Compra.AsNoTracking().AnyAsync(
            x => x.InstalacionId == installationId &&
                 x.Serie == series && x.Consecutivo != null,
            cancellationToken);

    public Task AddAsync(
        ConsecutivoDocumento numbering,
        CancellationToken cancellationToken = default) =>
        dbContext.ConsecutivoDocumento.AddAsync(numbering, cancellationToken).AsTask();
}

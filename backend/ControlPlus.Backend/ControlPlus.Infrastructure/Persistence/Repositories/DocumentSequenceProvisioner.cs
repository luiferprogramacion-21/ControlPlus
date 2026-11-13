using ControlPlus.Application.Numbering;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

/// <summary>
/// Creates the consecutive of a document type the first time it is needed, so no one has to configure it by
/// hand. It is a single idempotent statement: it takes part in the caller's transaction when there is one,
/// is safe when two requests arrive at the same time (the unique key decides) and never modifies a row
/// that already exists.
/// </summary>
internal static class DocumentSequenceProvisioner
{
    public static Task EnsureAsync(
        OfficialControlPlusDbContext dbContext, Guid installationId, string series, string documentType,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var prefix = DocumentNumberingDefaults.PrefixFor(documentType);
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO configuracion.consecutivo_documento
                 (id, instalacion_id, tipo_documento, prefijo, serie, ultimo_numero, version)
             VALUES ({id}, {installationId}, {documentType}, {prefix}, {series}, 0, 1)
             ON CONFLICT (instalacion_id, tipo_documento, serie) DO NOTHING
             """, cancellationToken);
    }
}

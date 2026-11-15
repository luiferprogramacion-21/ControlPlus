using ControlPlus.Application.Common;
using ControlPlus.Application.Numbering;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>
/// The consecutives and reason catalogs the backend creates by itself (installation and first use). These checks need
/// no database: the integration tests cover that the rows are really created and used.
/// </summary>
public sealed class AutomaticDefaultsTests
{
    [Fact]
    public void EveryDocumentTypeHasItsOwnFixedPrefix()
    {
        var all = DocumentNumberingDefaults.All;

        Assert.Equal(7, all.Count);
        Assert.Equal(all.Count, all.Select(x => x.DocumentType).Distinct().Count());
        Assert.Equal(all.Count, all.Select(x => x.Prefix).Distinct().Count());
        Assert.All(all, x =>
        {
            Assert.InRange(x.Prefix.Length, 1, 10);
            Assert.Equal(x.Prefix, DocumentNumberingDefaults.PrefixFor(x.DocumentType));
        });
        Assert.Equal(["ABO", "APT", "CAM", "CMP", "PAP", "PED", "VTA"],
            all.Select(x => x.Prefix).Order().ToArray());
    }

    [Fact]
    public void TheDocumentTypesMatchTheDatabaseConstraint()
    {
        // ck_consecutivo_tipo of configuracion.consecutivo_documento in the approved schema.
        string[] allowed =
            ["VENTA", "COMPRA", "APARTADO", "PAGO_CREDITO", "PAGO_APARTADO", "CAMBIO_VENTA", "PEDIDO_COMPRA"];

        Assert.Equal(allowed.Order(), DocumentNumberingDefaults.All.Select(x => x.DocumentType).Order());
    }

    [Fact]
    public void AnUnknownDocumentTypeHasNoDefaultConsecutive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentNumberingDefaults.PrefixFor("OTRO"));

    [Fact]
    public void EveryApprovedCatalogHasUniqueCodesAndNames()
    {
        Assert.All(ApprovedReasonCatalogs.All, catalog =>
        {
            Assert.NotEmpty(catalog.Entries);
            Assert.Equal(catalog.Entries.Length, catalog.Entries.Select(x => x.Code).Distinct().Count());
            Assert.Equal(catalog.Entries.Length, catalog.Entries.Select(x => x.Name).Distinct().Count());
            Assert.All(catalog.Entries, entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Code));
                Assert.False(string.IsNullOrWhiteSpace(entry.Name));
            });
        });
    }

    [Fact]
    public void BuildAll_ProducesExactlyTheRowsTheExplicitInitializationCreates()
    {
        var establishmentId = Guid.CreateVersion7();
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        var rows = ApprovedReasonCatalogs.BuildAll(establishmentId, now);

        Assert.Equal(ApprovedReasonCatalogs.All.Sum(x => x.Entries.Length), rows.Count);
        Assert.Equal(rows.Count, rows.Select(x => x.Id).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(x => (x.TipoOperacion, x.Codigo)).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.Equal(establishmentId, row.EstablecimientoId);
            Assert.True(row.Activo);
            Assert.Equal(1, row.Version);
            Assert.Equal(now, row.FechaCreacion);
            Assert.Equal(row.Nombre, row.Descripcion);
            Assert.Equal('7', row.Id.ToString("D")[14]);
        });
        // Inside each catalog the order is (position + 1) * 10, the rule the services use to recognise an
        // approved reason, so a seeded row is never taken for an incompatible one.
        foreach (var (operationType, entries) in ApprovedReasonCatalogs.All)
            Assert.Equal(Enumerable.Range(1, entries.Length).Select(i => i * 10),
                rows.Where(x => x.TipoOperacion == operationType).OrderBy(x => x.OrdenVisual)
                    .Select(x => x.OrdenVisual));
    }

    [Fact]
    public void TheCashDifferenceReasonsAreNotSeeded_BecauseTheCashScreenCreatesThemOnDemand()
    {
        Assert.DoesNotContain(ApprovedReasonCatalogs.All,
            x => x.OperationType == ControlPlus.Domain.OfficialModel.CashConstants.CashDifferenceReasonType);
    }
}

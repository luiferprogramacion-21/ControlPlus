using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class LayawayCancellationMigrationTests
{
    private const string Previous = "20261020010000_LayawayDeliveryWithoutSaleV1";
    private const string Target = "20261021010000_LayawayCancellationOptionalReasonV1";

    [Fact]
    public void Up_OnlyRelaxesCancellationReasonRequirement()
    {
        using var context = CreateContext();
        var script = context.Database.GetService<IMigrator>().GenerateScript(Previous, Target);
        Assert.Contains("DROP CONSTRAINT ck_apartado_transicion", script);
        Assert.Contains("estado = 'ANULADO'", script);
        Assert.Contains("AND venta_entrega_id IS NULL", script);
        Assert.DoesNotContain("AND venta_entrega_id IS NOT NULL", script);
        Assert.DoesNotContain("AND motivo_anulacion_id IS NOT NULL", script);
        Assert.Contains("valor_devuelto_cancelacion + valor_retenido_cancelacion", script);
        Assert.DoesNotContain("UPDATE ventas.apartado", script);
    }

    [Fact]
    public void Down_RestoresReasonRequirementWithoutChangingHistory()
    {
        using var context = CreateContext();
        var script = context.Database.GetService<IMigrator>().GenerateScript(Target, Previous);
        Assert.Contains("AND motivo_anulacion_id IS NOT NULL", script);
        Assert.DoesNotContain("AND venta_entrega_id IS NOT NULL", script);
        Assert.DoesNotContain("DELETE FROM ventas.apartado", script);
    }

    private static ControlPlusDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ControlPlusDbContext>().UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=controlplus_test_cancellation_script;Username=script_generation;Timeout=1")
            .Options);
}

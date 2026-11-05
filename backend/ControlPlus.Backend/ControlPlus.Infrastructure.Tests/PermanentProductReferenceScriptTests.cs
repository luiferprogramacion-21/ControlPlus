using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class PermanentProductReferenceScriptTests
{
    [Fact]
    public void UpgradeScript_KeepsHistoricalSnapshotsAndAddsPermanentLookupWithoutConnecting()
    {
        using var db = new ControlPlusDbContext(new DbContextOptionsBuilder<ControlPlusDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=controlplus_test_script;Username=script_generation").Options);
        var sql = db.GetService<IMigrator>().GenerateScript(
            "20261028010000_SaleReceiptDataSnapshotV1", "20261029010000_PermanentProductReferencesV1");
        Assert.Contains("catalogo.producto_codigo", sql);
        Assert.Contains("pg_advisory_xact_lock", sql);
        Assert.Contains("version = version + 1", sql);
        Assert.Contains("trg_producto_referencia_permanente", sql);
        Assert.DoesNotContain("UPDATE auditoria.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ventas.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE compras.", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }
}

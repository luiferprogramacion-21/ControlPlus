using System.IO.Compression;
using System.Text.RegularExpressions;
using ControlPlus.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ControlPlus.Infrastructure.Tests;

public sealed class LayawayDeliveryMigrationTests
{
    private const string Previous = "20261014010000_InventoryMovementRegistrationOrderV1";
    private const string Target = "20261020010000_LayawayDeliveryWithoutSaleV1";
    private const string SaleRequirement = "ANDVENTA_ENTREGA_IDISNOTNULL";

    [Fact]
    public void Migration_OnlyRemovesSaleRequirementFromDeliveredBranchWithoutChangingOtherConstraints()
    {
        using var context = CreateContext();
        var script = context.Database.GetService<IMigrator>().GenerateScript(Previous, Target);
        var original = Normalize(ConstraintExpression(LoadOfficialScript()));
        Assert.Equal(2, original.Split(SaleRequirement, StringSplitOptions.None).Length);
        var expected = original.Replace(SaleRequirement, "", StringComparison.Ordinal);

        Assert.Equal(expected, Normalize(ConstraintExpression(script)));
        AssertExactMigrationStatements(script, expected, reverting: false);
    }

    [Fact]
    public void Down_RestoresValidatedOfficialConstraintInOneTransactionWithoutRewritingData()
    {
        using var context = CreateContext();
        var script = context.Database.GetService<IMigrator>().GenerateScript(Target, Previous);
        var original = Normalize(ConstraintExpression(LoadOfficialScript()));

        Assert.Equal(original, Normalize(ConstraintExpression(script)));
        AssertExactMigrationStatements(script, original, reverting: true);
        Assert.DoesNotContain("NOT VALID", script, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertExactMigrationStatements(string script, string expression, bool reverting)
    {
        var statements = Regex.Replace(script, @"(?m)^\s*--[^\r\n]*", "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize).Where(x => x.Length != 0).ToArray();

        // Only the CHECK and EF's history row may change. In particular, no FK,
        // unique constraint, index, or historical business data may be rewritten.
        Assert.Equal(5, statements.Length);
        Assert.Equal("STARTTRANSACTION", statements[0]);
        Assert.Equal("ALTERTABLEVENTAS.APARTADODROPCONSTRAINTCK_APARTADO_TRANSICION", statements[1]);
        Assert.Equal("ALTERTABLEVENTAS.APARTADOADDCONSTRAINTCK_APARTADO_TRANSICIONCHECK(" + expression + ")",
            statements[2]);
        Assert.StartsWith(reverting ? "DELETEFROM__EFMIGRATIONSHISTORY" : "INSERTINTO__EFMIGRATIONSHISTORY",
            statements[3], StringComparison.Ordinal);
        Assert.Contains(Target.ToUpperInvariant(), statements[3], StringComparison.Ordinal);
        Assert.Equal("COMMIT", statements[4]);
    }

    private static string ConstraintExpression(string sql)
    {
        var constraint = Regex.Match(sql,
            "CONSTRAINT\\s+\"?ck_apartado_transicion\"?\\s+CHECK\\s*(?<open>\\()",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(constraint.Success, "The layaway transition CHECK was not found.");
        var opening = constraint.Groups["open"].Index;
        var depth = 1;
        var quoted = false;
        for (var index = opening + 1; index < sql.Length; index++)
        {
            if (sql[index] == '\'')
            {
                if (quoted && index + 1 < sql.Length && sql[index + 1] == '\'')
                    index++;
                else quoted = !quoted;
            }
            else if (!quoted)
            {
                if (sql[index] == '(') depth++;
                if (sql[index] == ')' && --depth == 0)
                    return sql[(opening + 1)..index];
            }
        }

        throw new InvalidOperationException("The layaway transition CHECK is incomplete.");
    }

    private static string LoadOfficialScript()
    {
        using var resource = typeof(ControlPlusDbContext).Assembly.GetManifestResourceStream(
            "ControlPlus.Infrastructure.Database.ControlPlus_Fase4_Documento_y_Complementos.zip")
            ?? throw new InvalidOperationException("The official Phase 4 package was not found.");
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries, x => x.FullName.EndsWith(
            "Script_Base_de_Datos_PostgreSQL_V3.sql", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string Normalize(string value) =>
        Regex.Replace(value, @"\s+", "").Replace("\"", "", StringComparison.Ordinal).ToUpperInvariant();

    private static ControlPlusDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ControlPlusDbContext>().UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=controlplus_test_delivery_script;Username=script_generation;Timeout=1")
            .Options);
}

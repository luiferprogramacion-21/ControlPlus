using System.Globalization;
using System.Text.RegularExpressions;
using ControlPlus.Api.Migrations;
using Npgsql;
using Xunit;

namespace ControlPlus.Api.Tests;

[Collection(ControlledMigrationCollection.Name)]
public sealed class ControlledInventoryMigrationTests(ControlledMigrationPostgresFixture postgres)
{
    private const string MeasurementMigration = "20260913010000_SeedMeasurementUnitsV1";
    private const string CashMigration = CashRegisterPreflightManifest.TargetMigration;
    private const string InventoryMigration = InventoryMigrationManifest.TargetMigration;

    [Fact]
    public void Manifest_DefinesTheExactInventoryContract()
    {
        Assert.Equal(InventoryMigration, InventoryMigrationManifest.TargetMigration);
        Assert.Equal(
            [
                "20260912010000_OfficialPhase4Baseline",
                "20260912011000_SeedApprovedSecurityCatalog",
                "20260912012000_HybridPermissionsV1",
                "20260913010000_SeedMeasurementUnitsV1",
                CashMigration
            ],
            InventoryMigrationManifest.RequiredPreviousMigrations);
        Assert.Equal(
            [
                "CONTEO_FISICO:10:Diferencia encontrada al contar existencias.",
                "MERMA_DANO:20:Producto da\u00f1ado o no vendible.",
                "PERDIDA:30:P\u00e9rdida o faltante.",
                "CORRECCION_REGISTRO:40:Correcci\u00f3n documentada de un registro previo."
            ],
            InventoryMigrationManifest.Reasons.Select(
                reason => $"{reason.Code}:{reason.DisplayOrder}:{reason.Name}"));
        Assert.All(
            InventoryMigrationManifest.Reasons,
            reason => Assert.Equal(reason.Name, reason.Description));
        Assert.Equal(
            ["establecimiento_id", "tipo_operacion", "codigo"],
            InventoryMigrationManifest.RequiredNaturalKeyConstraint.Columns);
        Assert.Equal(
            [
                "id:uuid::false",
                "establecimiento_id:uuid::false",
                "tipo_operacion:character varying:40:false",
                "codigo:character varying:50:false",
                "nombre:character varying:150:false",
                "descripcion:character varying:500:true",
                "orden_visual:integer::false",
                "activo:boolean::false",
                "fecha_creacion:timestamp with time zone::false",
                "fecha_modificacion:timestamp with time zone::true",
                "version:bigint::false"
            ],
            InventoryMigrationManifest.RequiredColumns.Select(
                column => $"{column.Name}:{column.DataType}:" +
                          $"{column.CharacterMaximumLength}:{column.IsNullable.ToString().ToLowerInvariant()}"));
    }

    [Fact]
    public async Task Preflight_WithFiveExactPredecessors_CertifiesMetricsAndRollsBackWithoutHttp()
    {
        var state = await CreateReadyDatabaseAsync(establishments: 2, includeUnrelatedReason: true);
        var before = await ReasonSnapshotAsync(state.ConnectionString);

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("REPEATABLE READ, READ ONLY", result.Output, StringComparison.Ordinal);
        Assert.Contains("E=2", result.Output, StringComparison.Ordinal);
        Assert.Contains("historial=5/5/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("motivos=0/0/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("motivos_previos=0", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "codigos=CONTEO_FISICO=0,MERMA_DANO=0,PERDIDA=0,CORRECCION_REGISTRO=0",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("uuid_incorrectos=0", result.Output, StringComparison.Ordinal);
        Assert.Contains("colisiones_uuid=0", result.Output, StringComparison.Ordinal);
        var fingerprint = ExtractFingerprint(result.Output);
        Assert.Equal(1L, fingerprint.Count);
        Assert.Contains("rollback=confirmed", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(before, await ReasonSnapshotAsync(state.ConnectionString));
        Assert.Equal(5L, await ControlledMigrationTests.HistoryCountAsync(state.ConnectionString));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(
            state.ConnectionString,
            InventoryMigration));
    }

    [Fact]
    public async Task Preflight_RejectsWhenCajaIsMissing()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, MeasurementMigration);

        var result = await RunAsync(
            connectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        AssertRejected(result, "five required predecessor migrations");
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
    }

    [Fact]
    public async Task Preflight_RejectsAppliedTargetExtraHistoryAndIncoherentHistory()
    {
        var applied = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ApplyAsync(applied.ConnectionString, InventoryMigration);
        var appliedResult = await RunAsync(
            applied.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);
        AssertRejected(appliedResult, "already applied");

        var extra = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            extra.ConnectionString,
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20990101000000_UnexpectedMigration', '10.0.3')
            """);
        var extraResult = await RunAsync(
            extra.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);
        AssertRejected(extraResult, "five required predecessor migrations");

        var incoherent = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            incoherent.ConnectionString,
            """
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20260912012000_HybridPermissionsV1'
            """);
        var incoherentResult = await RunAsync(
            incoherent.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);
        AssertRejected(incoherentResult, "five required predecessor migrations");
    }

    [Fact]
    public async Task Preflight_RejectsIncorrectTableOrNaturalKeyConstraintShape()
    {
        var tableState = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            tableState.ConnectionString,
            "ALTER TABLE configuracion.motivo_operacion ALTER COLUMN descripcion TYPE text");
        var tableResult = await RunAsync(
            tableState.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);
        AssertRejected(tableResult, "does not have the required shape");

        var constraintState = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            constraintState.ConnectionString,
            """
            ALTER TABLE configuracion.motivo_operacion
            DROP CONSTRAINT uq_motivo_operacion_tipo_codigo;
            ALTER TABLE configuracion.motivo_operacion
            ADD CONSTRAINT uq_motivo_operacion_tipo_codigo
            UNIQUE (establecimiento_id, codigo, tipo_operacion)
            """);
        var constraintResult = await RunAsync(
            constraintState.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);
        AssertRejected(constraintResult, "unique natural key shape");
    }

    [Theory]
    [InlineData("fecha_creacion", "DROP NOT NULL")]
    [InlineData("fecha_modificacion", "SET NOT NULL")]
    [InlineData("version", "DROP NOT NULL")]
    public async Task Preflight_RejectsEachMissingManifestColumnContract(
        string column,
        string alteration)
    {
        var state = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            state.ConnectionString,
            $"ALTER TABLE configuracion.motivo_operacion ALTER COLUMN {column} {alteration}");

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        AssertRejected(result, "does not have the required shape");
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(
            state.ConnectionString,
            InventoryMigration));
    }

    [Theory]
    [InlineData("CONTEO_FISICO")]
    [InlineData("MERMA_DANO")]
    [InlineData("PERDIDA")]
    [InlineData("CORRECCION_REGISTRO")]
    public async Task Preflight_RejectsEachPreexistingApprovedReasonGloballyAndByCode(string code)
    {
        var state = await CreateReadyDatabaseAsync(establishments: 1);
        await InsertReasonAsync(
            state.ConnectionString,
            Guid.CreateVersion7(),
            state.EstablishmentIds[0],
            InventoryMigrationManifest.OperationType,
            code,
            "Preexisting reason",
            99,
            active: false);

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        AssertRejected(result, "inventory reason preconditions failed");
        Assert.Contains("total=1", result.Output, StringComparison.Ordinal);
        Assert.Contains($"{code}=1", result.Output, StringComparison.Ordinal);
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(
            state.ConnectionString,
            InventoryMigration));
    }

    [Fact]
    public async Task Preflight_RejectsPartialApprovedCatalog()
    {
        var state = await CreateReadyDatabaseAsync(establishments: 2);
        await InsertReasonAsync(
            state.ConnectionString,
            Guid.CreateVersion7(),
            state.EstablishmentIds[0],
            InventoryMigrationManifest.OperationType,
            "CONTEO_FISICO",
            "Partial one",
            10,
            active: true);
        await InsertReasonAsync(
            state.ConnectionString,
            Guid.CreateVersion7(),
            state.EstablishmentIds[1],
            InventoryMigrationManifest.OperationType,
            "PERDIDA",
            "Partial two",
            30,
            active: true);

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        AssertRejected(result, "inventory reason preconditions failed");
        Assert.Contains("total=2", result.Output, StringComparison.Ordinal);
        Assert.Contains("CONTEO_FISICO=1", result.Output, StringComparison.Ordinal);
        Assert.Contains("PERDIDA=1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preflight_RejectsDeterministicUuidCollisionWithAnotherNaturalKey()
    {
        var state = await CreateReadyDatabaseAsync(establishments: 1);
        await ExecuteAsync(
            state.ConnectionString,
            """
            INSERT INTO configuracion.motivo_operacion(
                id, establecimiento_id, tipo_operacion, codigo, nombre, descripcion,
                orden_visual, activo, fecha_creacion, version)
            VALUES (
                md5(@prefix || @establishment::text || '.CONTEO_FISICO')::uuid,
                @establishment, 'DIFERENCIA_CIERRE', 'OTHER_NATURAL_KEY',
                'Unrelated collision', 'Must remain unrelated', 77, true, CURRENT_TIMESTAMP, 1)
            """,
            new NpgsqlParameter("prefix", InventoryMigrationManifest.DeterministicIdPrefix),
            new NpgsqlParameter("establishment", state.EstablishmentIds[0]));

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        AssertRejected(result, "inventory reason preconditions failed");
        Assert.Contains("colisiones_uuid=1", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preflight_RejectsApprovedNaturalKeyWithIncorrectUuid()
    {
        var state = await CreateReadyDatabaseAsync(establishments: 1);
        await InsertReasonAsync(
            state.ConnectionString,
            Guid.CreateVersion7(),
            state.EstablishmentIds[0],
            InventoryMigrationManifest.OperationType,
            "CONTEO_FISICO",
            "Wrong UUID",
            10,
            active: true);

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);

        AssertRejected(result, "inventory reason preconditions failed");
        Assert.Contains("CONTEO_FISICO=1", result.Output, StringComparison.Ordinal);
        Assert.Contains("uuid_incorrectos=1", result.Output, StringComparison.Ordinal);
        Assert.Contains("colisiones_uuid=0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_RequiresCajaAsFifthAndNeverAppliesCajaAndInventoryTogether()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, MeasurementMigration);

        var result = await RunAsync(
            connectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);

        AssertRejected(result, "requires Caja as the fifth and last applied migration");
        Assert.Equal(4L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(connectionString, CashMigration));
        Assert.Equal(0L, await ControlledMigrationTests.MigrationCountAsync(connectionString, InventoryMigration));
    }

    [Fact]
    public async Task Migration_WithExactCajaHistory_AppliesOnlyInventoryOnce()
    {
        var state = await CreateReadyDatabaseAsync(establishments: 2, includeUnrelatedReason: true);
        var unrelatedBefore = await UnrelatedReasonSnapshotAsync(state.ConnectionString);

        var result = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("applied successfully", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(6L, await ControlledMigrationTests.HistoryCountAsync(state.ConnectionString));
        Assert.Equal(1L, await ControlledMigrationTests.MigrationCountAsync(state.ConnectionString, CashMigration));
        Assert.Equal(1L, await ControlledMigrationTests.MigrationCountAsync(
            state.ConnectionString,
            InventoryMigration));
        Assert.Equal(8L, await ScalarAsync(
            state.ConnectionString,
            """
            SELECT count(*) FROM configuracion.motivo_operacion
            WHERE tipo_operacion='AJUSTE_INVENTARIO'
              AND codigo = ANY(@codes)
            """,
            new NpgsqlParameter(
                "codes",
                InventoryMigrationManifest.Reasons.Select(reason => reason.Code).ToArray())));
        Assert.Equal(unrelatedBefore, await UnrelatedReasonSnapshotAsync(state.ConnectionString));
    }

    [Fact]
    public async Task Migration_RejectsAppliedTargetAndExtraOrIncoherentHistory()
    {
        var applied = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ApplyAsync(applied.ConnectionString, InventoryMigration);
        var appliedResult = await RunAsync(
            applied.ConnectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);
        AssertRejected(appliedResult, "already applied");

        var extra = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            extra.ConnectionString,
            """
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20990101000000_UnexpectedMigration', '10.0.3')
            """);
        var extraResult = await RunAsync(
            extra.ConnectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);
        AssertRejected(extraResult, "not a coherent catalog prefix");

        var incoherent = await CreateReadyDatabaseAsync(establishments: 1);
        await ControlledMigrationTests.ExecuteAsync(
            incoherent.ConnectionString,
            """
            DELETE FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = '20260912012000_HybridPermissionsV1'
            """);
        var incoherentResult = await RunAsync(
            incoherent.ConnectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);
        AssertRejected(incoherentResult, "not a coherent catalog prefix");
    }

    [Fact]
    public async Task Postflight_WithMultipleEstablishments_CertifiesAllMetadataAndPreservesUnrelatedReasons()
    {
        var state = await CreateReadyDatabaseAsync(establishments: 3, includeUnrelatedReason: true);
        var unrelatedBefore = await UnrelatedReasonSnapshotAsync(state.ConnectionString);
        var expectedFingerprint = await RunSuccessfulPreflightAsync(state.ConnectionString);
        var migration = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);
        Assert.Equal(0, migration.ExitCode);

        var result = await RunPostflightAsync(
            state.ConnectionString,
            expectedFingerprint.Sha256);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("REPEATABLE READ, READ ONLY", result.Output, StringComparison.Ordinal);
        Assert.Contains("E=3", result.Output, StringComparison.Ordinal);
        Assert.Contains("historial=6/6/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("motivos=12/12/0", result.Output, StringComparison.Ordinal);
        Assert.Contains("motivos_ajenos=1", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            $"motivos_ajenos_sha256={expectedFingerprint.Sha256}",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("rollback=confirmed", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(unrelatedBefore, await UnrelatedReasonSnapshotAsync(state.ConnectionString));
        Assert.Equal(6L, await ControlledMigrationTests.HistoryCountAsync(state.ConnectionString));
    }

    [Fact]
    public async Task Postflight_RejectsMissingTargetAndInvalidReasonMetadata()
    {
        var missing = await CreateReadyDatabaseAsync(establishments: 1);
        var missingFingerprint = await RunSuccessfulPreflightAsync(missing.ConnectionString);
        var missingResult = await RunPostflightAsync(
            missing.ConnectionString,
            missingFingerprint.Sha256);
        AssertRejected(missingResult, "six required migrations");

        var invalid = await CreateReadyDatabaseAsync(establishments: 1);
        var invalidFingerprint = await RunSuccessfulPreflightAsync(invalid.ConnectionString);
        await ControlledMigrationTests.ApplyAsync(invalid.ConnectionString, InventoryMigration);
        await ControlledMigrationTests.ExecuteAsync(
            invalid.ConnectionString,
            """
            UPDATE configuracion.motivo_operacion
            SET activo=false, orden_visual=999
            WHERE tipo_operacion='AJUSTE_INVENTARIO' AND codigo='PERDIDA'
            """);
        var invalidResult = await RunPostflightAsync(
            invalid.ConnectionString,
            invalidFingerprint.Sha256);
        AssertRejected(invalidResult, "inventory reason verification failed");
    }

    [Theory]
    [InlineData("modify")]
    [InlineData("delete")]
    [InlineData("add")]
    public async Task Postflight_RejectsAnyUnrelatedReasonFingerprintChange(string change)
    {
        var state = await CreateReadyDatabaseAsync(establishments: 2, includeUnrelatedReason: true);
        var expectedFingerprint = await RunSuccessfulPreflightAsync(state.ConnectionString);
        var migration = await RunAsync(
            state.ConnectionString,
            ControlledExecutionCommand.MigrationTargetArgument,
            InventoryMigration);
        Assert.Equal(0, migration.ExitCode);

        switch (change)
        {
            case "modify":
                await ControlledMigrationTests.ExecuteAsync(
                    state.ConnectionString,
                    """
                    UPDATE configuracion.motivo_operacion
                    SET nombre='Changed without exposing it', version=version+1
                    WHERE codigo='UNRELATED_CONTROLLED_TEST'
                    """);
                break;
            case "delete":
                await ControlledMigrationTests.ExecuteAsync(
                    state.ConnectionString,
                    """
                    DELETE FROM configuracion.motivo_operacion
                    WHERE codigo='UNRELATED_CONTROLLED_TEST'
                    """);
                break;
            case "add":
                await InsertReasonAsync(
                    state.ConnectionString,
                    Guid.CreateVersion7(),
                    state.EstablishmentIds[1],
                    "DIFERENCIA_CIERRE",
                    "UNRELATED_ADDED_TEST",
                    "Added without exposing it",
                    74,
                    active: true);
                break;
            default:
                throw new InvalidOperationException($"Unsupported change {change}.");
        }

        var result = await RunPostflightAsync(
            state.ConnectionString,
            expectedFingerprint.Sha256);

        AssertRejected(result, "unrelated reasons fingerprint mismatch");
        Assert.DoesNotContain("Changed without exposing it", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Added without exposing it", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--preflight-to")]
    [InlineData("--migrate-to")]
    [InlineData("--postflight-to")]
    public async Task ControlledInventoryModes_ReturnSanitizedTechnicalFailureWithoutHttp(string mode)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        const string sentinel = "DO-NOT-LEAK-INVENTORY-PASSWORD";
        var invalidConnection = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Password = sentinel
        }.ConnectionString;

        var result = string.Equals(
                mode,
                ControlledExecutionCommand.PostflightTargetArgument,
                StringComparison.Ordinal)
            ? await RunPostflightAsync(invalidConnection, new string('0', 64))
            : await RunAsync(invalidConnection, mode, InventoryMigration);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("failed", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sentinel, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidConnection, result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", result.Output, StringComparison.Ordinal);
        Assert.False(result.HttpPortObserved);
        Assert.Equal(0L, await ControlledMigrationTests.HistoryCountAsync(connectionString));
    }

    private async Task<InventoryDatabaseState> CreateReadyDatabaseAsync(
        int establishments,
        bool includeUnrelatedReason = false)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ControlledMigrationTests.ApplyAsync(connectionString, CashMigration);
        var ids = new List<Guid>();
        for (var index = 0; index < establishments; index++)
        {
            var id = Guid.CreateVersion7();
            ids.Add(id);
            await ExecuteAsync(
                connectionString,
                """
                INSERT INTO configuracion.establecimiento(
                    id, nombre_comercial, identificacion, zona_horaria,
                    porcentaje_minimo_apartado, dias_limite_cambio,
                    modo_turno_predeterminado, rpo_minutos, rto_minutos,
                    dias_prueba_restauracion, activo, fecha_creacion, version)
                VALUES (
                    @id, @name, @identifier, 'America/Bogota', 20, 15,
                    'INDIVIDUAL', 30, 120, 90, true, CURRENT_TIMESTAMP, 1)
                """,
                new NpgsqlParameter("id", id),
                new NpgsqlParameter("name", $"Inventory migration test {index}"),
                new NpgsqlParameter("identifier", $"INV-{Guid.NewGuid():N}"[..30]));
        }

        if (includeUnrelatedReason)
        {
            await InsertReasonAsync(
                connectionString,
                Guid.CreateVersion7(),
                ids[0],
                "DIFERENCIA_CIERRE",
                "UNRELATED_CONTROLLED_TEST",
                "Unrelated reason",
                73,
                active: false);
        }

        return new InventoryDatabaseState(connectionString, ids);
    }

    private static Task<ControlledMigrationTests.MigrationProcessResult> RunAsync(
        string? connectionString,
        string mode,
        string target) =>
        ControlledMigrationTests.RunApiProcessAsync(connectionString, "true", mode, target);

    private static Task<ControlledMigrationTests.MigrationProcessResult> RunPostflightAsync(
        string? connectionString,
        string expectedSha256) =>
        ControlledMigrationTests.RunApiProcessAsync(
            connectionString,
            "true",
            ControlledExecutionCommand.PostflightTargetArgument,
            InventoryMigration,
            ControlledExecutionCommand.ExpectedUnrelatedReasonsSha256Argument,
            expectedSha256);

    private static async Task<UnrelatedReasonsFingerprint> RunSuccessfulPreflightAsync(
        string connectionString)
    {
        var result = await RunAsync(
            connectionString,
            ControlledExecutionCommand.PreflightTargetArgument,
            InventoryMigration);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.HttpPortObserved);
        return ExtractFingerprint(result.Output);
    }

    private static UnrelatedReasonsFingerprint ExtractFingerprint(string output)
    {
        var match = Regex.Match(
            output,
            @"motivos_ajenos=(\d+); motivos_ajenos_sha256=([0-9a-f]{64})",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, output);
        return new UnrelatedReasonsFingerprint(
            long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            match.Groups[2].Value);
    }

    private static async Task InsertReasonAsync(
        string connectionString,
        Guid id,
        Guid establishmentId,
        string operationType,
        string code,
        string name,
        int displayOrder,
        bool active) =>
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO configuracion.motivo_operacion(
                id, establecimiento_id, tipo_operacion, codigo, nombre, descripcion,
                orden_visual, activo, fecha_creacion, version)
            VALUES (
                @id, @establishment, @operationType, @code, @name, @description,
                @displayOrder, @active, CURRENT_TIMESTAMP, 1)
            """,
            new NpgsqlParameter("id", id),
            new NpgsqlParameter("establishment", establishmentId),
            new NpgsqlParameter("operationType", operationType),
            new NpgsqlParameter("code", code),
            new NpgsqlParameter("name", name),
            new NpgsqlParameter("description", $"{name} description"),
            new NpgsqlParameter("displayOrder", displayOrder),
            new NpgsqlParameter("active", active));

    private static async Task ExecuteAsync(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(
        string connectionString,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReasonSnapshotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(string_agg(
                id::text || '|' || establecimiento_id::text || '|' || tipo_operacion || '|' ||
                codigo || '|' || nombre || '|' || coalesce(descripcion, '') || '|' ||
                orden_visual::text || '|' || activo::text || '|' || version::text,
                E'\n' ORDER BY id), '')
            FROM configuracion.motivo_operacion
            """,
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> UnrelatedReasonSnapshotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT id::text || '|' || establecimiento_id::text || '|' || tipo_operacion || '|' ||
                   codigo || '|' || nombre || '|' || coalesce(descripcion, '') || '|' ||
                   orden_visual::text || '|' || activo::text || '|' || version::text
            FROM configuracion.motivo_operacion
            WHERE codigo='UNRELATED_CONTROLLED_TEST'
            """,
            connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static void AssertRejected(
        ControlledMigrationTests.MigrationProcessResult result,
        string expectedMessage)
    {
        Assert.Equal(2, result.ExitCode);
        Assert.Contains(expectedMessage, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.HttpPortObserved);
    }

    private sealed record InventoryDatabaseState(
        string ConnectionString,
        IReadOnlyList<Guid> EstablishmentIds);

    private sealed record UnrelatedReasonsFingerprint(long Count, string Sha256);
}

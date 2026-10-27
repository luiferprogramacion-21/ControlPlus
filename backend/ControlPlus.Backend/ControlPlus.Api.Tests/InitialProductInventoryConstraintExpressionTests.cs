using System.Reflection;
using ControlPlus.Api.Migrations;
using Xunit;

namespace ControlPlus.Api.Tests;

public sealed class InitialProductInventoryConstraintExpressionTests
{
    private const string Official = "((tipo_movimiento)::text = ANY ((ARRAY[" +
        "'COMPRA'::character varying, 'ANULACION_COMPRA'::character varying, 'VENTA'::character varying, " +
        "'ANULACION_VENTA'::character varying, 'RESERVA_APARTADO'::character varying, " +
        "'LIBERACION_APARTADO'::character varying, 'ENTREGA_APARTADO'::character varying, " +
        "'CAMBIO_DEVOLUCION'::character varying, 'CAMBIO_ENTREGA'::character varying, " +
        "'AJUSTE_ENTRADA'::character varying, 'AJUSTE_SALIDA'::character varying, " +
        "'REVERSO'::character varying])::text[]))";

    [Fact]
    public void OfficialPostgresExpression_IsAccepted() => Assert.True(Evaluate(Official));

    [Theory]
    [InlineData("COM PRA")]
    [InlineData("COM(PRA")]
    [InlineData("COMPRA::text")]
    [InlineData("COMP''RA")]
    public void AlteredLiteral_IsRejectedEvenWhenItContainsNormalizationCharacters(string literal) =>
        Assert.False(Evaluate(Official.Replace("'COMPRA'", $"'{literal}'", StringComparison.Ordinal)));

    [Fact]
    public void AdditionalExpression_IsRejected() => Assert.False(Evaluate(Official + " OR true"));

    private static bool Evaluate(string expression)
    {
        var type = typeof(ControlledPreflightRunner).Assembly.GetType(
            "ControlPlus.Api.Migrations.InitialProductInventoryPreflightRunner", throwOnError: true)!;
        var method = type.GetMethod("IsOfficialMovementTypeExpression", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [expression])!;
    }
}

using ControlPlus.Application.Common;
using Xunit;

namespace ControlPlus.Api.Tests;

/// <summary>Decisión D4 (23/11/2026): sin efectivo en la caja se mantiene el rechazo, pero se dice cuánto falta y qué hacer.</summary>
public sealed class CashShortageTests
{
    [Fact]
    public void Error_SaysHowMuchIsNeededHowMuchThereIsAndWhatToDo()
    {
        var error = CashShortage.Error("anular este abono", 50_000m, 12_500m, otherMethodsAllowed: false);
        Assert.Equal("resource.conflict", error.Code);
        Assert.Equal("Para anular este abono hay que entregar $50.000 en efectivo, pero la caja abierta solo tiene $12.500. " +
            "Registra en Caja un ingreso de efectivo por la diferencia (por ejemplo, «Fondo para devolución») y vuelve a intentarlo.",
            error.Message);
    }

    [Fact]
    public void Error_OffersAnotherMethodWhenTheRefundAllowsIt_AndNeverShowsNegativeCash()
    {
        var error = CashShortage.Error("devolver el dinero del apartado", 1_000m, -300m, otherMethodsAllowed: true);
        Assert.Contains("solo tiene $0.", error.Message);
        Assert.Contains("Devuelve por Nequi o transferencia", error.Message);
    }
}

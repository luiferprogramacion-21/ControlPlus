using System.Globalization;

namespace ControlPlus.Application.Common;

/// <summary>
/// Mensaje único para cuando la caja abierta no tiene el efectivo que hay que entregar (decisión D4 de la auditoría del
/// 23 de noviembre de 2026: se mantiene la regla, pero el mensaje dice cuánto falta y qué hacer).
/// </summary>
public static class CashShortage
{
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");

    private static string Money(decimal value) => "$" + decimal.Round(value, 0).ToString("N0", Colombia);

    /// <param name="action">Lo que se intenta, en infinitivo: «anular este abono», «devolver el dinero del apartado».</param>
    /// <param name="needed">Efectivo que habría que entregar.</param>
    /// <param name="available">Efectivo que la caja abierta espera tener.</param>
    /// <param name="otherMethodsAllowed">Si la devolución puede hacerse por Nequi o transferencia en lugar de efectivo.</param>
    public static ApplicationError Error(string action, decimal needed, decimal available, bool otherMethodsAllowed) =>
        ApplicationError.Conflict(
            $"Para {action} hay que entregar {Money(needed)} en efectivo, pero la caja abierta solo tiene {Money(Math.Max(0, available))}. " +
            (otherMethodsAllowed
                ? "Devuelve por Nequi o transferencia, o registra en Caja un ingreso de efectivo y vuelve a intentarlo."
                : "Registra en Caja un ingreso de efectivo por la diferencia (por ejemplo, «Fondo para devolución») y vuelve a intentarlo."));
}

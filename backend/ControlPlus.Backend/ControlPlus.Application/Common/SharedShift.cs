namespace ControlPlus.Application.Common;

/// <summary>
/// Mensaje único del turno compartido cuando nadie ha pasado su carné en la terminal (pruebas del usuario del 25 de
/// noviembre de 2026: el mensaje dice qué hacer, en palabras de tienda). Las pantallas piden el carné antes de la
/// operación; este mensaje queda para cuando la sesión terminó entre tanto.
/// </summary>
public static class SharedShift
{
    public const string CardRequiredMessage =
        "El turno es compartido: primero pasa tu carné para identificarte (en Caja, «Carné del operador») y vuelve a intentarlo.";

    public static ApplicationError CardRequired() => ApplicationError.Conflict(CardRequiredMessage);
}

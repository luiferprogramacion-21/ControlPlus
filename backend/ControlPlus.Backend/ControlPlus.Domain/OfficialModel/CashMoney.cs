namespace ControlPlus.Domain.OfficialModel;

/// <summary>Whole COP values representable by PostgreSQL numeric(18,0).</summary>
public static class CashMoney
{
    public const decimal Maximum = 999999999999999999m;

    public static bool IsValid(decimal value, bool allowNegative = true) =>
        value >= (allowNegative ? -Maximum : 0m) && value <= Maximum && decimal.Truncate(value) == value;

    public static decimal Require(decimal value, bool allowNegative = true)
    {
        if (!IsValid(value, allowNegative)) throw new CashMoneyException();
        return value;
    }

    public static decimal Add(decimal left, decimal right)
    {
        Require(left);
        Require(right);
        try { return Require(checked(left + right)); }
        catch (OverflowException) { throw new CashMoneyException(); }
    }

    public static decimal Subtract(decimal left, decimal right)
    {
        Require(left);
        Require(right);
        try { return Require(checked(left - right)); }
        catch (OverflowException) { throw new CashMoneyException(); }
    }

    public static decimal Sum(IEnumerable<decimal> values)
    {
        var total = 0m;
        foreach (var value in values) total = Add(total, value);
        return total;
    }
}

public sealed class CashMoneyException() : InvalidOperationException(
    "El importe o acumulado de Caja debe ser un valor entero dentro del rango numeric(18,0).")
{
    public const string ErrorCode = "cash.amount.invalid";
}

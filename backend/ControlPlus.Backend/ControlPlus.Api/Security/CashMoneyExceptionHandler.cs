using ControlPlus.Domain.OfficialModel;
using Microsoft.AspNetCore.Diagnostics;

namespace ControlPlus.Api.Security;

public sealed class CashMoneyExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CashMoneyException) return false;
        await Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Los importes y sus acumulados deben ser pesos enteros dentro del rango monetario permitido.",
            type: "https://controlplus.local/problems/cash-amount-invalid",
            instance: context.Request.Path,
            extensions: new Dictionary<string, object?> { ["code"] = CashMoneyException.ErrorCode })
            .ExecuteAsync(context);
        return true;
    }
}

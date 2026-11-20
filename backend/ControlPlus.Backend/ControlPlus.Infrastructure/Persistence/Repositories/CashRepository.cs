using System.Data;
using System.Security.Cryptography;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Cash.Ports;
using ControlPlus.Application.Common;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfCashTransactionManager(OfficialControlPlusDbContext dbContext)
    : ICashTransactionManager
{
    public async Task<IApplicationTransaction> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.Serializable,
        CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        return new EfApplicationTransaction(transaction);
    }

    private sealed class EfApplicationTransaction(IDbContextTransaction transaction)
        : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

/// <summary>
/// Produces an opaque, Code 128-compatible value with 72 bits of entropy, written as a 22-digit decimal number so a
/// scanner reads it in Code 128 subset C (two digits per symbol: about 40 % shorter than hexadecimal, so the bars stay
/// wide enough on a card). Since 20 November 2026 it is 22 digits (40 on 19 November, 58 before): printed on the 80 mm
/// receipt roll with 0.375 mm bars (3 thermal dots), because 2-dot bars spread when the head heats and a laser scanner
/// (Honeywell Voyager MS9520 at the store) could not read the card at all. Earlier: the card is printed on the 80 mm
/// receipt roll and 58 digits forced bars thinner than one dot of the thermal head. Cards already issued keep working (only
/// the hash is compared). The caller is responsible for returning it only once and persisting only its hash.
/// </summary>
public sealed class Code128CredentialTokenGenerator : ICredentialTokenGenerator
{
    private const int EntropyBytes = 9;
    private const int Digits = 22;

    public string Generate() =>
        new System.Numerics.BigInteger(RandomNumberGenerator.GetBytes(EntropyBytes), isUnsigned: true, isBigEndian: true)
            .ToString().PadLeft(Digits, '0');
}

public sealed partial class EfCashRepository(OfficialControlPlusDbContext dbContext) : ICashRepository
{
    public async Task<Instalacion?> GetActiveInstallationForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Instalacion
            .FromSqlRaw(
                """
                SELECT i.*
                FROM configuracion.instalacion AS i
                WHERE i.activo
                ORDER BY i.fecha_instalacion, i.id
                LIMIT 1
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public async Task<Establecimiento?> GetEstablishmentAsync(
        bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
        {
            return await dbContext.Establecimiento
                .Where(establishment => establishment.Activo)
                .OrderBy(establishment => establishment.FechaCreacion)
                .ThenBy(establishment => establishment.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rows = await dbContext.Establecimiento
            .FromSqlRaw(
                """
                SELECT e.*
                FROM configuracion.establecimiento AS e
                WHERE e.activo
                ORDER BY e.fecha_creacion, e.id
                LIMIT 1
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public Task<Terminal?> GetActiveTerminalAsync(
        Guid installationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Terminal
            .Where(terminal => terminal.InstalacionId == installationId && terminal.Activo)
            .OrderBy(terminal => terminal.FechaCreacion)
            .ThenBy(terminal => terminal.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Caja?> GetCashRegisterAsync(
        bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
        {
            return await dbContext.Caja
                .Where(cashRegister => cashRegister.Instalacion.Activo)
                .OrderBy(cashRegister => cashRegister.FechaCreacion)
                .ThenBy(cashRegister => cashRegister.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rows = await dbContext.Caja
            .FromSqlRaw(
                """
                SELECT c.*
                FROM caja.caja AS c
                INNER JOIN configuracion.instalacion AS i ON i.id = c.instalacion_id
                WHERE i.activo
                ORDER BY c.fecha_creacion, c.id
                LIMIT 1
                FOR UPDATE OF c
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public Task AddCashRegisterAsync(Caja cashRegister, CancellationToken cancellationToken = default) =>
        dbContext.Caja.AddAsync(cashRegister, cancellationToken).AsTask();

    public async Task<TurnoCaja?> GetOpenShiftAsync(
        bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        if (!forUpdate)
        {
            return await dbContext.TurnoCaja
                .Where(shift => shift.Estado == CashConstants.OpenState)
                .OrderBy(shift => shift.FechaHoraApertura)
                .ThenBy(shift => shift.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rows = await dbContext.TurnoCaja
            .FromSqlRaw(
                """
                SELECT t.*
                FROM caja.turno_caja AS t
                WHERE t.estado = 'ABIERTA'
                ORDER BY t.fecha_hora_apertura, t.id
                LIMIT 1
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public async Task<TurnoCaja?> GetShiftForUpdateAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.TurnoCaja
            .FromSqlInterpolated(
                $"""
                SELECT t.*
                FROM caja.turno_caja AS t
                WHERE t.id = {shiftId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public Task AddShiftAsync(TurnoCaja shift, CancellationToken cancellationToken = default) =>
        dbContext.TurnoCaja.AddAsync(shift, cancellationToken).AsTask();

    public void MarkShiftChanged(TurnoCaja shift) =>
        dbContext.Entry(shift).Property(candidate => candidate.Version).IsModified = true;

    public async Task<PagedResult<MovimientoCaja>> ListMovementsAsync(
        Guid shiftId,
        Guid? userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MovimientoCaja
            .AsNoTracking()
            .Where(movement => movement.TurnoCajaId == shiftId);

        if (userId is Guid requestedUserId)
        {
            query = query.Where(movement => movement.UsuarioId == requestedUserId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(movement => movement.FechaHora)
            .ThenByDescending(movement => movement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new PagedResult<MovimientoCaja>(items, page, pageSize, totalCount);
    }

    public Task AddMovementAsync(
        MovimientoCaja movement,
        CancellationToken cancellationToken = default) =>
        dbContext.MovimientoCaja.AddAsync(movement, cancellationToken).AsTask();

    public async Task<IReadOnlyCollection<ExpectedPaymentMethodAmount>> GetExpectedAmountsAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default)
    {
        var movements = await dbContext.MovimientoCaja.AsNoTracking()
            .Where(movement => movement.TurnoCajaId == shiftId)
            .OrderBy(movement => movement.FechaHora).ThenBy(movement => movement.Id)
            .Select(movement => new MovementAmountRow(
                movement.Id, movement.Tipo, movement.Valor, movement.MovimientoRevertidoId,
                movement.MovimientoRevertido == null ? null : movement.MovimientoRevertido.Tipo,
                movement.PagoVenta.Any() || movement.PagoCredito.Any() || movement.PagoApartado.Any() ||
                movement.PagoCambioVenta.Any() || movement.ReembolsoApartado.Any()))
            .ToArrayAsync(cancellationToken);
        var reversedIds = movements.Where(movement => movement.ReversedMovementId.HasValue)
            .Select(movement => movement.ReversedMovementId!.Value).ToArray();

        // Payments are the only source for amounts attributed to payment methods.
        // Read original payments of reversals too, even when they belong to an earlier shift.
        var payments = new List<PaymentAmountRow>();
        payments.AddRange(await dbContext.PagoVenta.AsNoTracking()
            .Where(payment => payment.Venta.TurnoCajaId == shiftId ||
                (payment.MovimientoCajaId.HasValue && reversedIds.Contains(payment.MovimientoCajaId.Value)))
            .Select(payment => new PaymentAmountRow(payment.MetodoPagoId, payment.ValorAplicado,
                payment.Venta.TurnoCajaId, payment.MovimientoCajaId, payment.Estado,
                payment.MovimientoCaja != null && payment.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.PagoCredito.AsNoTracking()
            .Where(payment => payment.TurnoCajaId == shiftId ||
                (payment.MovimientoCajaId.HasValue && reversedIds.Contains(payment.MovimientoCajaId.Value)))
            .Select(payment => new PaymentAmountRow(payment.MetodoPagoId, payment.ValorAplicado,
                payment.TurnoCajaId, payment.MovimientoCajaId, payment.Estado,
                payment.MovimientoCaja != null && payment.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.PagoApartado.AsNoTracking()
            .Where(payment => payment.TurnoCajaId == shiftId ||
                (payment.MovimientoCajaId.HasValue && reversedIds.Contains(payment.MovimientoCajaId.Value)))
            .Select(payment => new PaymentAmountRow(payment.MetodoPagoId, payment.ValorAplicado,
                payment.TurnoCajaId, payment.MovimientoCajaId, payment.Estado,
                payment.MovimientoCaja != null && payment.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.PagoCambioVenta.AsNoTracking()
            .Where(payment => payment.TurnoCajaId == shiftId ||
                (payment.MovimientoCajaId.HasValue && reversedIds.Contains(payment.MovimientoCajaId.Value)))
            .Select(payment => new PaymentAmountRow(payment.MetodoPagoId,
                payment.TipoMovimiento == "COBRO" ? payment.Valor : -payment.Valor,
                payment.TurnoCajaId, payment.MovimientoCajaId, payment.Estado,
                payment.MovimientoCaja != null && payment.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));
        payments.AddRange(await dbContext.ReembolsoApartado.AsNoTracking()
            .Where(payment => payment.TurnoCajaId == shiftId ||
                (payment.MovimientoCajaId.HasValue && reversedIds.Contains(payment.MovimientoCajaId.Value)))
            .Select(payment => new PaymentAmountRow(payment.MetodoPagoId, -payment.Valor,
                payment.TurnoCajaId, payment.MovimientoCajaId, "APLICADO",
                payment.MovimientoCaja != null && payment.MovimientoCaja.InverseMovimientoRevertido != null))
            .ToArrayAsync(cancellationToken));

        var usedPaymentMethodIds = payments.Select(payment => payment.PaymentMethodId).Distinct().ToArray();
        var methods = await dbContext.MetodoPago
            .AsNoTracking()
            .Where(method => method.Activo || usedPaymentMethodIds.Contains(method.Id))
            .OrderBy(method => method.OrdenVisual)
            .ThenBy(method => method.Codigo)
            .Select(method => new PaymentMethodRow(
                method.Id,
                method.Codigo,
                method.Nombre,
                method.AfectaEfectivo))
            .ToArrayAsync(cancellationToken);

        if (methods.Length == 0)
        {
            return Array.Empty<ExpectedPaymentMethodAmount>();
        }

        var paymentExpected = methods.ToDictionary(method => method.Id, _ => 0m);
        foreach (var payment in payments.Where(payment => payment.ShiftId == shiftId &&
                     (payment.State == "APLICADO" || (payment.State == "ANULADO" && payment.HasReversal))))
        {
            AddAmount(paymentExpected, payment.PaymentMethodId, payment.Amount);
        }

        var manualCash = 0m;
        foreach (var movement in movements.Where(movement => !movement.HasPayments))
        {
            var originalPayments = payments.Where(payment => payment.MovementId == movement.ReversedMovementId).ToArray();
            if (movement.Type == "REVERSO" && originalPayments.Length > 0)
            {
                // The official model cannot allocate a partial reversal across combined methods.
                // Require a full reversal; keep the original contribution and add its explicit inverse.
                var originalTotal = CashMoney.Sum(originalPayments.Select(payment => Math.Abs(payment.Amount)));
                if (movement.Amount != originalTotal)
                    throw new CashMoneyException();
                foreach (var payment in originalPayments)
                    AddAmount(paymentExpected, payment.PaymentMethodId, CashMoney.Subtract(0m, payment.Amount));
            }
            else
            {
                var signedAmount = movement.Type is "APERTURA" or "INGRESO" ||
                    (movement.Type == "REVERSO" && movement.ReversedType == "EGRESO")
                        ? movement.Amount : CashMoney.Subtract(0m, movement.Amount);
                manualCash = CashMoney.Add(manualCash, signedAmount);
            }
        }

        // Manual physical cash belongs to EFECTIVO exactly once, even if another method affects cash.
        var cashMethod = methods.SingleOrDefault(method => method.Code == "EFECTIVO" && method.AffectsCash);
        if (manualCash != 0m)
        {
            if (cashMethod is null) throw new CashMoneyException();
            AddAmount(paymentExpected, cashMethod.Id, manualCash);
        }

        var result = methods
            .Select(method => new ExpectedPaymentMethodAmount(
                method.Id,
                method.Code,
                method.Name,
                method.AffectsCash,
                CashMoney.Require(paymentExpected[method.Id], allowNegative: !method.AffectsCash)))
            .ToArray();
        CashMoney.Sum(result.Select(method => method.ExpectedAmount));
        return result;
    }

    public Task AddReconciliationDetailsAsync(
        IReadOnlyCollection<DetalleArqueoMedioPago> details,
        CancellationToken cancellationToken = default) =>
        dbContext.DetalleArqueoMedioPago.AddRangeAsync(details, cancellationToken);

    public Task<MotivoOperacion?> GetActiveDifferenceReasonAsync(
        Guid reasonId,
        CancellationToken cancellationToken = default) =>
        dbContext.MotivoOperacion.SingleOrDefaultAsync(
            reason => reason.Id == reasonId &&
                      reason.Activo &&
                      reason.Establecimiento.Activo &&
                      reason.TipoOperacion == CashConstants.CashDifferenceReasonType,
            cancellationToken);

    public async Task<IReadOnlyCollection<MotivoOperacion>> GetDifferenceReasonsAsync(
        Guid establishmentId,
        CancellationToken cancellationToken = default) =>
        await dbContext.MotivoOperacion
            .Where(reason => reason.EstablecimientoId == establishmentId &&
                             reason.TipoOperacion == CashConstants.CashDifferenceReasonType)
            .ToArrayAsync(cancellationToken);

    public Task AddDifferenceReasonsAsync(
        IReadOnlyCollection<MotivoOperacion> reasons,
        CancellationToken cancellationToken = default) =>
        dbContext.MotivoOperacion.AddRangeAsync(reasons, cancellationToken);

    public async Task<IReadOnlyCollection<MotivoOperacion>> ListActiveDifferenceReasonsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.MotivoOperacion.AsNoTracking()
            .Where(reason => reason.Activo && reason.Establecimiento.Activo &&
                             reason.TipoOperacion == CashConstants.CashDifferenceReasonType)
            .OrderBy(reason => reason.OrdenVisual)
            .ThenBy(reason => reason.Codigo)
            .ToArrayAsync(cancellationToken);

    public Task AddAuthorizationAsync(
        AutorizacionOperacion authorization,
        CancellationToken cancellationToken = default) =>
        dbContext.AutorizacionOperacion.AddAsync(authorization, cancellationToken).AsTask();

    public Task AddCloseAuthorizationLinkAsync(
        AutorizacionCierreTurno link,
        CancellationToken cancellationToken = default) =>
        dbContext.Set<AutorizacionCierreTurno>().AddAsync(link, cancellationToken).AsTask();

    public async Task<Usuario?> GetUserForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Usuario
            .FromSqlInterpolated(
                $"""
                SELECT u.*
                FROM seguridad.usuario AS u
                WHERE u.id = {userId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyCollection<CredencialUsuario>> ListActiveCredentialsAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.CredencialUsuario
            .AsNoTracking()
            .Where(credential => credential.Estado == "ACTIVA")
            .OrderBy(credential => credential.UsuarioId)
            .ToArrayAsync(cancellationToken);

    public async Task<CredencialUsuario?> GetActiveCredentialForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default,
        bool forUpdate = false)
    {
        if (!forUpdate)
            return await dbContext.CredencialUsuario.SingleOrDefaultAsync(
                credential => credential.UsuarioId == userId && credential.Estado == "ACTIVA", cancellationToken);

        var rows = await dbContext.CredencialUsuario.FromSqlInterpolated(
                $"""
                SELECT credential.*
                FROM seguridad.credencial_usuario AS credential
                WHERE credential.usuario_id = {userId} AND credential.estado = 'ACTIVA'
                FOR UPDATE
                """)
            .AsTracking().ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task AddCredentialAsync(
        CredencialUsuario credential,
        CancellationToken cancellationToken = default) =>
        dbContext.CredencialUsuario.AddAsync(credential, cancellationToken).AsTask();

    public async Task<SesionOperador?> GetActiveOperatorSessionForUpdateAsync(
        Guid terminalId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.SesionOperador
            .FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM caja.sesion_operador AS s
                WHERE s.terminal_id = {terminalId}
                  AND s.estado = 'ACTIVA'
                LIMIT 1
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public async Task<SesionOperador?> GetOperatorSessionForUpdateAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.SesionOperador
            .FromSqlInterpolated(
                $"""
                SELECT s.*
                FROM caja.sesion_operador AS s
                WHERE s.id = {sessionId}
                FOR UPDATE
                """)
            .AsTracking()
            .ToArrayAsync(cancellationToken);

        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyCollection<SesionOperador>> ListActiveOperatorSessionsAsync(
        Guid shiftId,
        CancellationToken cancellationToken = default) =>
        await dbContext.SesionOperador
            .Where(session => session.TurnoCajaId == shiftId && session.Estado == CashConstants.ActiveSessionState)
            .OrderBy(session => session.FechaInicio)
            .ToArrayAsync(cancellationToken);

    public Task AddOperatorSessionAsync(
        SesionOperador session,
        CancellationToken cancellationToken = default) =>
        dbContext.SesionOperador.AddAsync(session, cancellationToken).AsTask();

    private static void AddAmount(IDictionary<Guid, decimal> totals, Guid methodId, decimal amount) =>
        totals[methodId] = CashMoney.Add(totals[methodId], amount);

    private sealed record PaymentMethodRow(Guid Id, string Code, string Name, bool AffectsCash);

    private sealed record PaymentAmountRow(Guid PaymentMethodId, decimal Amount, Guid ShiftId,
        Guid? MovementId, string State, bool HasReversal);

    private sealed record MovementAmountRow(Guid Id, string Type, decimal Amount,
        Guid? ReversedMovementId, string? ReversedType, bool HasPayments);
}

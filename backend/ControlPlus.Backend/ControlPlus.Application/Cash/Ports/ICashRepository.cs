using System.Data;
using ControlPlus.Application.Cash.Contracts;
using ControlPlus.Application.Common;
using ControlPlus.Domain.OfficialModel;

namespace ControlPlus.Application.Cash.Ports;

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface ICashTransactionManager
{
    Task<IApplicationTransaction> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.Serializable,
        CancellationToken cancellationToken = default);
}

public interface ICredentialTokenGenerator
{
    string Generate();
}

public interface ICashRepository
{
    Task<Instalacion?> GetActiveInstallationForUpdateAsync(CancellationToken cancellationToken = default);
    Task<Establecimiento?> GetEstablishmentAsync(bool forUpdate, CancellationToken cancellationToken = default);
    Task<Terminal?> GetActiveTerminalAsync(Guid installationId, CancellationToken cancellationToken = default);
    Task<Caja?> GetCashRegisterAsync(bool forUpdate, CancellationToken cancellationToken = default);
    Task AddCashRegisterAsync(Caja cashRegister, CancellationToken cancellationToken = default);

    Task<TurnoCaja?> GetOpenShiftAsync(bool forUpdate, CancellationToken cancellationToken = default);
    Task<TurnoCaja?> GetShiftForUpdateAsync(Guid shiftId, CancellationToken cancellationToken = default);
    Task AddShiftAsync(TurnoCaja shift, CancellationToken cancellationToken = default);
    void MarkShiftChanged(TurnoCaja shift);

    Task<PagedResult<MovimientoCaja>> ListMovementsAsync(Guid shiftId, Guid? userId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task AddMovementAsync(MovimientoCaja movement, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ExpectedPaymentMethodAmount>> GetExpectedAmountsAsync(Guid shiftId, CancellationToken cancellationToken = default);
    Task AddReconciliationDetailsAsync(IReadOnlyCollection<DetalleArqueoMedioPago> details, CancellationToken cancellationToken = default);

    Task<MotivoOperacion?> GetActiveDifferenceReasonAsync(Guid reasonId, CancellationToken cancellationToken = default);
    Task AddAuthorizationAsync(AutorizacionOperacion authorization, CancellationToken cancellationToken = default);
    Task AddCloseAuthorizationLinkAsync(AutorizacionCierreTurno link, CancellationToken cancellationToken = default);

    Task<Usuario?> GetUserForUpdateAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CredencialUsuario>> ListActiveCredentialsAsync(CancellationToken cancellationToken = default);
    Task<CredencialUsuario?> GetActiveCredentialForUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddCredentialAsync(CredencialUsuario credential, CancellationToken cancellationToken = default);

    Task<SesionOperador?> GetActiveOperatorSessionForUpdateAsync(Guid terminalId, CancellationToken cancellationToken = default);
    Task<SesionOperador?> GetOperatorSessionForUpdateAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<SesionOperador>> ListActiveOperatorSessionsAsync(Guid shiftId, CancellationToken cancellationToken = default);
    Task AddOperatorSessionAsync(SesionOperador session, CancellationToken cancellationToken = default);
}

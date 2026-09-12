using ControlPlus.Application.Security.Contracts;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Security;

public sealed class InstallationBootstrapper(OfficialControlPlusDbContext dbContext)
{
    public async Task EnsureCreatedAsync(
        SetupFirstAdministratorRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.Instalacion.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var establishment = new Establecimiento
        {
            Id = Guid.CreateVersion7(),
            NombreComercial = Required(request.EstablishmentName, nameof(request.EstablishmentName)),
            Identificacion = Required(request.EstablishmentIdentification, nameof(request.EstablishmentIdentification)),
            ZonaHoraria = "America/Bogota",
            PorcentajeMinimoApartado = 20m,
            DiasLimiteCambio = 15,
            ModoTurnoPredeterminado = "INDIVIDUAL",
            RpoMinutos = 30,
            RtoMinutos = 120,
            DiasPruebaRestauracion = 90,
            Activo = true,
            FechaCreacion = now,
            Version = 1
        };
        var installation = new Instalacion
        {
            Id = Guid.CreateVersion7(),
            EstablecimientoId = establishment.Id,
            Codigo = Required(request.InstallationCode, nameof(request.InstallationCode)),
            Serie = Required(request.InstallationSerial, nameof(request.InstallationSerial)),
            FechaInstalacion = now,
            VersionAplicacion = typeof(InstallationBootstrapper).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            VersionEsquema = "V3",
            Activo = true,
            Version = 1,
            Establecimiento = establishment
        };
        var terminal = new Terminal
        {
            Id = Guid.CreateVersion7(),
            InstalacionId = installation.Id,
            Codigo = Required(request.TerminalCode, nameof(request.TerminalCode)),
            Nombre = Required(request.TerminalName, nameof(request.TerminalName)),
            Activo = true,
            FechaCreacion = now,
            Version = 1,
            Instalacion = installation
        };

        await dbContext.Establecimiento.AddAsync(establishment, cancellationToken);
        await dbContext.Instalacion.AddAsync(installation, cancellationToken);
        await dbContext.Terminal.AddAsync(terminal, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty installation value is required.", name)
            : value.Trim();
}

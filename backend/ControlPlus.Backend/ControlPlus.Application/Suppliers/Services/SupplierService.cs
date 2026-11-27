using System.Net.Mail;
using ControlPlus.Application.Common;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Application.Suppliers.Contracts;
using ControlPlus.Application.Suppliers.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Suppliers.Services;

public sealed class SupplierService(ISupplierRepository repository, IUserRepository users,
    IAuditRepository audit, IUnitOfWork unitOfWork, IClock clock,
    IPasswordHasher? passwordHasher = null, IPermissionChecker? permissionChecker = null,
    IAuthorizerCredentialResolver? credentialResolver = null, ILoginAttemptCoordinator? loginAttemptCoordinator = null) : ISupplierService
{
    public async Task<Result<PagedResult<SupplierDto>>> ListAsync(ActorContext actor, SupplierListQuery query, CancellationToken ct = default)
    {
        if (!await HasAsync(actor, PermissionCodes.SuppliersRead, false, ct)) return Denied<PagedResult<SupplierDto>>();
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            return Invalid<PagedResult<SupplierDto>>("La página debe ser positiva y el tamaño debe estar entre 1 y 100.");
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (offset > int.MaxValue) return Invalid<PagedResult<SupplierDto>>("La página solicitada supera el límite permitido.");
        var name = Optional(query.Name)?.ToLowerInvariant();
        var type = Optional(query.DocumentType)?.ToUpperInvariant();
        var number = Optional(query.DocumentNumber)?.ToUpperInvariant();
        if (name?.Length > 200 || type?.Length > 20 || number?.Length > 50 || (type is null) != (number is null))
            return Invalid<PagedResult<SupplierDto>>("Los filtros de nombre o documento son inválidos.");
        var normalized = query with { Name = name, DocumentType = type, DocumentNumber = number, IsActive = query.IsActive ?? true };
        var result = await repository.ListAsync(normalized, (int)offset, ct);
        return Result.Success(new PagedResult<SupplierDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<SupplierDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        if (!await HasAsync(actor, PermissionCodes.SuppliersRead, false, ct)) return Denied<SupplierDto>();
        var supplier = await repository.GetAsync(id, ct);
        return supplier is null ? Result.Failure<SupplierDto>(ApplicationError.NotFound("el proveedor")) : Result.Success(Map(supplier));
    }

    public async Task<Result<SupplierDto>> CreateAsync(ActorContext actor, CreateSupplierRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await HasAsync(actor, PermissionCodes.SuppliersManage, true, ct))
        {
            if (request.Authorization is not null)
                return Invalid<SupplierDto>("Quien crea proveedores con su propio permiso no envía autorización.");
            var input = Validate(request.DocumentType, request.DocumentNumber, request.Name, request.Phone,
                request.Address, request.Email, request.Observations);
            if (input.Error is not null) return Result.Failure<SupplierDto>(input.Error);
            await using var transaction = await repository.BeginAsync(ct);
            var created = await AddSupplierAsync(actor, input, null, null, ct);
            await transaction.CommitAsync(ct);
            return created;
        }

        // Pruebas del usuario del 26/11/2026: el Supervisor que recibe mercancía crea el proveedor de una compra sin pedido
        // con la autorización del Administrador (su carné, o su usuario y contraseña).
        if (!await IsReceivingSupervisorAsync(actor, ct) || passwordHasher is null || permissionChecker is null ||
            credentialResolver is null || loginAttemptCoordinator is null)
            return Denied<SupplierDto>();
        if (request.Authorization is null)
            return Result.Failure<SupplierDto>(ApplicationError.Forbidden(
                "Para crear un proveedor, un Supervisor necesita la autorización del Administrador (carné o usuario y contraseña)."));
        var supervisorInput = Validate(request.DocumentType, request.DocumentNumber, request.Name, request.Phone,
            request.Address, request.Email, request.Observations);
        if (supervisorInput.Error is not null) return Result.Failure<SupplierDto>(supervisorInput.Error);
        var authorization = request.Authorization;
        if (authorization.UsesCredential())
        {
            if (authorization.MixesCredentialWithPassword())
                return Invalid<SupplierDto>("Envíe el carné escaneado o usuario y contraseña, no ambos.");
            var (scannedUserName, scannedCredentialId) = await credentialResolver.ResolveOrUnknownAsync(authorization.CredentialToken!, ct);
            authorization = authorization with { UserName = scannedUserName, ResolvedCredentialId = scannedCredentialId };
        }
        if (!authorization.IsComplete())
            return Invalid<SupplierDto>("Indique usuario y contraseña del Administrador o escanee su carné.");
        var account = authorization.UserName!.Trim().ToUpperInvariant();
        return await loginAttemptCoordinator.ExecuteAsync(account, async token =>
        {
            var validated = await AuthorizerValidator.ValidateAsync(users, passwordHasher, permissionChecker, audit, clock, actor,
                authorization, PermissionCodes.SuppliersManage, RoleLevel.Administrador, ApplicationError.Forbidden(
                    "Solo un Administrador activo autoriza crear un proveedor. Revise el usuario y la contraseña o escanee su carné."),
                token);
            if (validated.IsFailure)
            {
                await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.SupplierCreated, nameof(Proveedor), null,
                    new { Failure = "INVALID_AUTHORIZER_CREDENTIALS_OR_PERMISSION" }, token);
                await unitOfWork.SaveChangesAsync(token);
                return Result.Failure<SupplierDto>(validated.Error!);
            }
            return await AddSupplierAsync(actor, supervisorInput, validated.Value!.UserName,
                authorization.UsesCredential() ? "CARNE" : "CONTRASENA", token);
        }, ct);
    }

    private async Task<Result<SupplierDto>> AddSupplierAsync(ActorContext actor,
        (string? Type, string? Number, string? Name, string? Phone, string? Address, string? Email, string? Observations, ApplicationError? Error) input,
        string? authorizedBy, string? authorizedWith, CancellationToken ct)
    {
        var supplier = new Proveedor
        {
            Id = Guid.CreateVersion7(), TipoDocumento = input.Type, NumeroDocumento = input.Number,
            NombreRazonSocial = input.Name!, Telefono = input.Phone, Direccion = input.Address,
            Correo = input.Email, Observaciones = input.Observations, Activo = true,
            UsuarioCreacionId = actor.UserId, FechaCreacion = clock.UtcNow.UtcDateTime, Version = 1
        };
        await repository.AddAsync(supplier, ct);
        if (authorizedBy is null) await WriteAuditAsync(actor.UserId, AuditAction.SupplierCreated, supplier, ["isActive"], 1, ct);
        else
            await AuditWriter.WriteAsync(audit, clock, actor.UserId, AuditAction.SupplierCreated, nameof(Proveedor), supplier.Id,
                new { supplierId = supplier.Id, modifiedFields = new[] { "isActive" }, isActive = supplier.Activo, version = 1L,
                    AuthorizedBy = authorizedBy, AuthorizedWith = authorizedWith }, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(supplier));
    }

    /// <summary>Supervisor vigente con permiso de recibir mercancía y de ver proveedores.</summary>
    private async Task<bool> IsReceivingSupervisorAsync(ActorContext actor, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        return snapshot is not null && snapshot.Roles.Any(role => role.Code == RoleCodes.Supervisor) &&
            snapshot.PermissionCodes.Contains(PermissionCodes.SuppliersRead, StringComparer.OrdinalIgnoreCase) &&
            snapshot.PermissionCodes.Contains(PermissionCodes.PurchasesReceive, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Result<SupplierDto>> UpdateAsync(ActorContext actor, Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        if (!await HasAsync(actor, PermissionCodes.SuppliersManage, true, ct)) return Denied<SupplierDto>();
        if (request.ExpectedVersion <= 0) return Invalid<SupplierDto>("La versión esperada debe ser positiva.");
        var input = Validate(request.DocumentType, request.DocumentNumber, request.Name, request.Phone,
            request.Address, request.Email, request.Observations);
        if (input.Error is not null) return Result.Failure<SupplierDto>(input.Error);
        var supplier = await repository.GetAsync(id, ct);
        if (supplier is null) return Result.Failure<SupplierDto>(ApplicationError.NotFound("el proveedor"));
        if (supplier.Version != request.ExpectedVersion) return Stale();
        var fields = Changed(supplier, input);
        await using var transaction = await repository.BeginAsync(ct);
        supplier.TipoDocumento = input.Type;
        supplier.NumeroDocumento = input.Number;
        supplier.NombreRazonSocial = input.Name!;
        supplier.Telefono = input.Phone;
        supplier.Direccion = input.Address;
        supplier.Correo = input.Email;
        supplier.Observaciones = input.Observations;
        supplier.UsuarioModificacionId = actor.UserId;
        supplier.FechaModificacion = clock.UtcNow.UtcDateTime;
        await WriteAuditAsync(actor.UserId, AuditAction.SupplierUpdated, supplier, fields, checked(request.ExpectedVersion + 1), ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(supplier));
    }

    public async Task<Result<SupplierDto>> SetStatusAsync(ActorContext actor, Guid id, SetSupplierStatusRequest request, CancellationToken ct = default)
    {
        if (!await HasAsync(actor, PermissionCodes.SuppliersManage, true, ct)) return Denied<SupplierDto>();
        if (request.ExpectedVersion <= 0) return Invalid<SupplierDto>("La versión esperada debe ser positiva.");
        var supplier = await repository.GetAsync(id, ct);
        if (supplier is null) return Result.Failure<SupplierDto>(ApplicationError.NotFound("el proveedor"));
        if (supplier.Version != request.ExpectedVersion) return Stale();
        await using var transaction = await repository.BeginAsync(ct);
        supplier.Activo = request.IsActive;
        supplier.UsuarioModificacionId = actor.UserId;
        supplier.FechaModificacion = clock.UtcNow.UtcDateTime;
        await WriteAuditAsync(actor.UserId, request.IsActive ? AuditAction.SupplierReactivated : AuditAction.SupplierDeactivated,
            supplier, ["isActive"], checked(request.ExpectedVersion + 1), ct);
        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success(Map(supplier));
    }

    private async Task<bool> HasAsync(ActorContext actor, string permission, bool requireAdministrator, CancellationToken ct)
    {
        var snapshot = await users.GetAuthorizationSnapshotAsync(actor.UserId, ct);
        return snapshot is not null && (!requireAdministrator ||
            snapshot.Roles.Any(role => role.Code == RoleCodes.Administrator)) &&
            snapshot.PermissionCodes.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    private Task WriteAuditAsync(Guid actorId, AuditAction action, Proveedor supplier,
        IReadOnlyCollection<string> fields, long version, CancellationToken ct) =>
        AuditWriter.WriteAsync(audit, clock, actorId, action, nameof(Proveedor), supplier.Id,
            new { supplierId = supplier.Id, modifiedFields = fields, isActive = supplier.Activo, version }, ct);

    private static (string? Type, string? Number, string? Name, string? Phone, string? Address,
        string? Email, string? Observations, ApplicationError? Error) Validate(
        string? type, string? number, string? name, string? phone, string? address,
        string? email, string? observations)
    {
        type = Optional(type)?.ToUpperInvariant();
        number = Optional(number)?.ToUpperInvariant();
        name = Optional(name);
        phone = Optional(phone);
        address = Optional(address);
        email = Optional(email)?.ToLowerInvariant();
        observations = Optional(observations);
        ApplicationError? error = null;
        if (name is null || name.Length > 200) error = ApplicationError.Validation("El nombre es obligatorio y no puede superar 200 caracteres.");
        else if ((type is null) != (number is null) || type?.Length > 20 || number?.Length > 50)
            error = ApplicationError.Validation("El documento debe informarse como pareja y respetar sus longitudes.");
        else if (phone?.Length > 30 || address?.Length > 300 || email?.Length > 254)
            error = ApplicationError.Validation("Teléfono, dirección o correo supera la longitud permitida.");
        else if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            error = ApplicationError.Validation("El correo no tiene un formato válido.");
        return (type, number, name, phone, address, email, observations, error);
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] Changed(Proveedor supplier,
        (string? Type, string? Number, string? Name, string? Phone, string? Address,
         string? Email, string? Observations, ApplicationError? Error) input) =>
        new[] { (supplier.TipoDocumento != input.Type, "documentType"),
            (supplier.NumeroDocumento != input.Number, "documentNumber"),
            (supplier.NombreRazonSocial != input.Name, "name"),
            (supplier.Telefono != input.Phone, "phone"),
            (supplier.Direccion != input.Address, "address"),
            (supplier.Correo != input.Email, "email"),
            (supplier.Observaciones != input.Observations, "observations") }
        .Where(item => item.Item1).Select(item => item.Item2).ToArray();

    private static SupplierDto Map(Proveedor supplier) => new(supplier.Id, supplier.TipoDocumento,
        supplier.NumeroDocumento, supplier.NombreRazonSocial, supplier.Telefono, supplier.Direccion,
        supplier.Correo, supplier.Observaciones, supplier.Activo, supplier.Version);
    private static Result<T> Denied<T>() => Result.Failure<T>(ApplicationError.Forbidden());
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(ApplicationError.Validation(message));
    private static Result<SupplierDto> Stale() => Result.Failure<SupplierDto>(
        ApplicationError.ConcurrencyConflict("El proveedor fue modificado. Recargue los datos."));
}

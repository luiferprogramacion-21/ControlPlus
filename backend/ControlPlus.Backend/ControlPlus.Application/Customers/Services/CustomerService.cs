using System.Net.Mail;
using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Customers.Ports;
using ControlPlus.Application.Security.Contracts;
using ControlPlus.Application.Security.Ports;
using ControlPlus.Application.Security.Services;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Domain.Security.Enums;

namespace ControlPlus.Application.Customers.Services;

public sealed class CustomerService(ICustomerRepository repository, IPermissionChecker permissions,
    IAuditRepository audit, IUnitOfWork unitOfWork, IClock clock) : ICustomerService
{
    public async Task<Result<PagedResult<CustomerDto>>> ListAsync(ActorContext actor, CustomerListQuery query, CancellationToken ct = default)
    {
        if (!await HasBasicAccessAsync(actor, ct)) return Denied<PagedResult<CustomerDto>>();
        if (query.Page < 1 || query.PageSize is < 1 or > 100) return Result.Failure<PagedResult<CustomerDto>>(ApplicationError.Validation("La página debe ser mayor que cero y el tamaño debe estar entre 1 y 100."));
        var status = (query.Status ?? "active").Trim().ToLowerInvariant();
        if (status is not ("active" or "inactive" or "all")) return Result.Failure<PagedResult<CustomerDto>>(ApplicationError.Validation("El estado debe ser active, inactive o all."));
        if (status is "inactive" or "all" && !await HasAsync(actor, PermissionCodes.CustomersManage, ct)) return Denied<PagedResult<CustomerDto>>();
        var result = await repository.ListAsync(query, status, ct);
        return Result.Success(new PagedResult<CustomerDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount));
    }

    public async Task<Result<CustomerDto>> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        if (!await HasBasicAccessAsync(actor, ct)) return Denied<CustomerDto>();
        var customer = await repository.GetAsync(id, ct);
        if (customer is null || (!customer.Activo && !await HasAsync(actor, PermissionCodes.CustomersManage, ct))) return Result.Failure<CustomerDto>(ApplicationError.NotFound("el cliente"));
        return Result.Success(Map(customer));
    }

    public async Task<Result<CustomerDto>> CreateAsync(ActorContext actor, CreateCustomerRequest request, CancellationToken ct = default)
    {
        if (!await HasBasicAccessAsync(actor, ct)) return Denied<CustomerDto>();
        var full = await HasAsync(actor, PermissionCodes.CustomersManage, ct);
        // El documento es opcional y lo puede escribir quien tiene el permiso básico (decisión del 18 de noviembre de 2026);
        // las observaciones y el cupo siguen reservados al permiso completo.
        if (!full && (request.Observations is not null ||
                      request.CreditLimit is not null))
        {
            return Denied<CustomerDto>();
        }
        var input = Validate(request.DocumentType, request.DocumentNumber, request.FullName, request.Phone, request.Address, request.Email, request.Observations);
        if (input.Error is not null) return Result.Failure<CustomerDto>(input.Error);
        if (CreditLimitError(request.CreditLimit) is { } limitError) return Result.Failure<CustomerDto>(limitError);
        if (input.Type is not null && await repository.DocumentExistsAsync(input.Type, input.Number!, null, ct)) return Result.Failure<CustomerDto>(ApplicationError.Conflict("Ya existe un cliente con ese documento."));
        var now = clock.UtcNow.UtcDateTime;
        var customer = new Cliente { Id = Guid.CreateVersion7(), TipoDocumento = input.Type, NumeroDocumento = input.Number, NombreCompleto = input.Name!, Telefono = input.Phone, Direccion = input.Address, Correo = input.Email, Observaciones = input.Observations, CupoCredito = request.CreditLimit, Activo = true, UsuarioCreacionId = actor.UserId, FechaCreacion = now, Version = 1 };
        await repository.AddAsync(customer, ct);
        await WriteAudit(actor.UserId, AuditAction.CustomerCreated, customer, ["activo"], ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(customer));
    }

    public async Task<Result<CustomerDto>> UpdateAsync(ActorContext actor, Guid id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        if (!await HasBasicAccessAsync(actor, ct)) return Denied<CustomerDto>();
        if (request.ExpectedVersion <= 0) return Result.Failure<CustomerDto>(ApplicationError.Validation("La versión esperada debe ser mayor que cero."));
        var input = Validate(request.DocumentType, request.DocumentNumber, request.FullName, request.Phone, request.Address, request.Email, request.Observations);
        if (input.Error is not null) return Result.Failure<CustomerDto>(input.Error);
        if (CreditLimitError(request.CreditLimit) is { } limitError) return Result.Failure<CustomerDto>(limitError);
        var customer = await repository.GetAsync(id, ct);
        if (customer is null) return Result.Failure<CustomerDto>(ApplicationError.NotFound("el cliente"));
        var full = await HasAsync(actor, PermissionCodes.CustomersManage, ct);
        if (!customer.Activo && !full) return Denied<CustomerDto>();
        if (customer.Version != request.ExpectedVersion) return Result.Failure<CustomerDto>(Conflict);
        if (!full && (customer.Observaciones != input.Observations || customer.CupoCredito != request.CreditLimit)) return Denied<CustomerDto>();
        if (input.Type is not null && await repository.DocumentExistsAsync(input.Type, input.Number!, customer.Id, ct)) return Result.Failure<CustomerDto>(ApplicationError.Conflict("Ya existe un cliente con ese documento."));
        var changed = Changed(customer, input);
        if (customer.CupoCredito != request.CreditLimit) changed = [.. changed, "cupoCredito"];
        customer.CupoCredito = request.CreditLimit;
        customer.TipoDocumento = input.Type; customer.NumeroDocumento = input.Number; customer.NombreCompleto = input.Name!; customer.Telefono = input.Phone; customer.Direccion = input.Address; customer.Correo = input.Email; customer.Observaciones = input.Observations; customer.UsuarioModificacionId = actor.UserId; customer.FechaModificacion = clock.UtcNow.UtcDateTime;
        await WriteAudit(actor.UserId, AuditAction.CustomerUpdated, customer, changed, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(customer));
    }

    public async Task<Result<CustomerDto>> SetStatusAsync(ActorContext actor, Guid id, SetCustomerStatusRequest request, CancellationToken ct = default)
    {
        if (!await HasAsync(actor, PermissionCodes.CustomersManage, ct)) return Denied<CustomerDto>();
        if (request.ExpectedVersion <= 0) return Result.Failure<CustomerDto>(ApplicationError.Validation("La versión esperada debe ser mayor que cero."));
        var customer = await repository.GetAsync(id, ct);
        if (customer is null) return Result.Failure<CustomerDto>(ApplicationError.NotFound("el cliente"));
        if (customer.Version != request.ExpectedVersion) return Result.Failure<CustomerDto>(Conflict);
        customer.Activo = request.IsActive; customer.UsuarioModificacionId = actor.UserId; customer.FechaModificacion = clock.UtcNow.UtcDateTime;
        await WriteAudit(actor.UserId, request.IsActive ? AuditAction.CustomerReactivated : AuditAction.CustomerDeactivated, customer, ["activo"], ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(Map(customer));
    }

    private async Task WriteAudit(Guid actor, AuditAction action, Cliente customer, IReadOnlyCollection<string> fields, CancellationToken ct) =>
        await AuditWriter.WriteAsync(audit, clock, actor, action, nameof(Cliente), customer.Id, new { customerId = customer.Id, modifiedFields = fields, active = customer.Activo }, ct);
    private async Task<bool> HasAsync(ActorContext actor, string permission, CancellationToken ct) => await permissions.HasPermissionAsync(actor.UserId, permission, ct);
    private async Task<bool> HasBasicAccessAsync(ActorContext actor, CancellationToken ct) =>
        await HasAsync(actor, PermissionCodes.CustomersBasicManage, ct) ||
        await HasAsync(actor, PermissionCodes.CustomersManage, ct);
    private static Result<T> Denied<T>() => Result.Failure<T>(ApplicationError.Forbidden());
    private static (string? Type, string? Number, string? Name, string? Phone, string? Address, string? Email, string? Observations, ApplicationError? Error) Validate(string? type, string? number, string? name, string? phone, string? address, string? email, string? observations)
    {
        type = Optional(type)?.ToUpperInvariant(); number = Optional(number)?.ToUpperInvariant(); name = Optional(name); phone = Optional(phone); address = Optional(address); email = Optional(email)?.ToLowerInvariant(); observations = Optional(observations);
        if (name is null || name.Length > 200) return (null,null,null,null,null,null,null,ApplicationError.Validation("El nombre completo es obligatorio y no puede superar 200 caracteres."));
        // Desde el 21 de noviembre de 2026 la dirección es opcional al registrar; el crédito y el apartado la exigen.
        if (address?.Length > 300) return (null,null,null,null,null,null,null,ApplicationError.Validation("La dirección no puede superar 300 caracteres."));
        if (phone?.Length > 30 || email?.Length > 254) return (null,null,null,null,null,null,null,ApplicationError.Validation("El teléfono o correo supera la longitud permitida."));
        if ((type is null) != (number is null)) return (null,null,null,null,null,null,null,ApplicationError.Validation("El tipo y número de documento deben informarse juntos o ambos ser nulos."));
        if (type is not null && type is not ("CC" or "CE" or "NIT" or "PASAPORTE" or "PPT" or "OTRO")) return (null,null,null,null,null,null,null,ApplicationError.Validation("El tipo de documento no es válido."));
        if (number?.Length > 50) return (null,null,null,null,null,null,null,ApplicationError.Validation("El número de documento no puede superar 50 caracteres."));
        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)) return (null,null,null,null,null,null,null,ApplicationError.Validation("El correo no tiene un formato válido."));
        return (type, number, name, phone, address, email, observations, null);
    }
    private static string? Optional(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private static IReadOnlyCollection<string> Changed(Cliente x, (string? Type,string? Number,string? Name,string? Phone,string? Address,string? Email,string? Observations,ApplicationError? Error) i) => new[] { (x.TipoDocumento != i.Type,"tipoDocumento"), (x.NumeroDocumento != i.Number,"numeroDocumento"), (x.NombreCompleto != i.Name,"nombreCompleto"), (x.Telefono != i.Phone,"telefono"), (x.Direccion != i.Address,"direccion"), (x.Correo != i.Email,"correo"), (x.Observaciones != i.Observations,"observaciones") }.Where(x => x.Item1).Select(x => x.Item2).ToArray();
    private static CustomerDto Map(Cliente x) => new(x.Id, x.TipoDocumento, x.NumeroDocumento, x.NombreCompleto, x.Telefono, x.Direccion, x.Correo, x.Observaciones, x.Activo, x.Version, x.CupoCredito);

    /// <summary>Cupo de crédito (16 de noviembre de 2026): vacío o pesos enteros positivos.</summary>
    private static ApplicationError? CreditLimitError(decimal? limit) =>
        limit is null || (limit > 0 && limit <= 999_999_999_999_999_999m && decimal.Truncate(limit.Value) == limit) ? null
            : ApplicationError.Validation("El cupo de crédito debe ser un valor en pesos enteros mayor que cero, o vacío para no tener límite.");
    private static readonly ApplicationError Conflict = ApplicationError.ConcurrencyConflict("El registro fue modificado por otro usuario. Recargue los datos e intente nuevamente.");
}

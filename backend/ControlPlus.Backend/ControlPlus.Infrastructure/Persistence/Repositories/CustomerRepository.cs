using ControlPlus.Application.Common;
using ControlPlus.Application.Customers.Contracts;
using ControlPlus.Application.Customers.Ports;
using ControlPlus.Domain.OfficialModel;
using ControlPlus.Infrastructure.Persistence.Official;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence.Repositories;

public sealed class EfCustomerRepository(OfficialControlPlusDbContext dbContext) : ICustomerRepository
{
    private const string LikeEscapeCharacter = "\\";

    public async Task<PagedResult<Cliente>> ListAsync(CustomerListQuery query, string status, CancellationToken cancellationToken = default)
    {
        var customers = dbContext.Cliente.AsNoTracking().AsQueryable();
        if (status == "active") customers = customers.Where(x => x.Activo);
        else if (status == "inactive") customers = customers.Where(x => !x.Activo);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var namePattern = $"{EscapeLikePattern(term.ToLowerInvariant())}%";
            var documentPattern = $"{EscapeLikePattern(term.ToUpperInvariant())}%";
            var phonePattern = $"{EscapeLikePattern(term.ToLowerInvariant())}%";
            customers = customers.Where(x =>
                EF.Functions.Like(x.NombreCompleto.ToLower(), namePattern, LikeEscapeCharacter) ||
                (x.NumeroDocumento != null &&
                 EF.Functions.Like(x.NumeroDocumento, documentPattern, LikeEscapeCharacter)) ||
                (x.Telefono != null &&
                 EF.Functions.Like(x.Telefono.ToLower(), phonePattern, LikeEscapeCharacter)));
        }
        var total = await customers.CountAsync(cancellationToken);
        var items = await customers.OrderBy(x => x.NombreCompleto).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(cancellationToken);
        return new PagedResult<Cliente>(items, query.Page, query.PageSize, total);
    }

    public Task<Cliente?> GetAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        dbContext.Cliente.SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);

    public Task<bool> DocumentExistsAsync(string documentType, string documentNumber, Guid? excludedId = null, CancellationToken cancellationToken = default) =>
        dbContext.Cliente.AnyAsync(x => x.TipoDocumento == documentType && x.NumeroDocumento == documentNumber &&
            (excludedId == null || x.Id != excludedId), cancellationToken);

    public Task AddAsync(Cliente customer, CancellationToken cancellationToken = default) => dbContext.Cliente.AddAsync(customer, cancellationToken).AsTask();

    private static string EscapeLikePattern(string value) =>
        value.Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);
}

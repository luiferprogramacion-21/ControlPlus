using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence;

/// <summary>
/// Migration-only context retained because the immutable published migrations are
/// associated with this context type. Runtime repositories use OfficialControlPlusDbContext.
/// </summary>
public sealed class ControlPlusDbContext(DbContextOptions<ControlPlusDbContext> options) : DbContext(options)
{
}

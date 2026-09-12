using ControlPlus.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlPlus.Infrastructure.Persistence;

/// <summary>
/// Persistence boundary for ControlPlus. The security mappings are provisional until
/// they are reconciled with the approved Phase 4 data model.
/// </summary>
public sealed class ControlPlusDbContext(DbContextOptions<ControlPlusDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ControlPlusDbContext).Assembly);
    }
}

using ControlPlus.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ControlPlus.Infrastructure.Persistence.Configurations;

/// <summary>
/// Provisional EF Core mapping for the security aggregate. Reconcile table and column
/// names, constraints, and cardinalities with the approved Phase 4 model before the
/// first migration is created.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("security_users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.UserName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.NormalizedUserName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(user => user.SecurityStamp).HasMaxLength(64).IsRequired();
        builder.Property(user => user.Status).IsRequired();
        builder.Property(user => user.FailedLoginAttempts).IsRequired();
        builder.Property(user => user.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(user => user.UpdatedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(user => user.LockedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(user => user.LastLoginAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
        builder.HasIndex(user => user.Status);

        builder.HasMany(user => user.UserRoles)
            .WithOne(userRole => userRole.User)
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(user => user.UserRoles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("security_roles");
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id).ValueGeneratedNever();
        builder.Property(role => role.Code).HasMaxLength(64).IsRequired();
        builder.Property(role => role.Name).HasMaxLength(100).IsRequired();
        builder.Property(role => role.Level).IsRequired();
        builder.Property(role => role.IsActive).IsRequired();
        builder.Property(role => role.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(role => role.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(role => role.Code).IsUnique();
        builder.HasIndex(role => role.IsActive);

        builder.HasMany(role => role.RolePermissions)
            .WithOne(rolePermission => rolePermission.Role)
            .HasForeignKey(rolePermission => rolePermission.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(role => role.RolePermissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("security_permissions");
        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Id).ValueGeneratedNever();
        builder.Property(permission => permission.Code).HasMaxLength(100).IsRequired();
        builder.Property(permission => permission.Name).HasMaxLength(160).IsRequired();
        builder.Property(permission => permission.Description).HasMaxLength(500);
        builder.Property(permission => permission.IsActive).IsRequired();
        builder.Property(permission => permission.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(permission => permission.UpdatedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasIndex(permission => permission.Code).IsUnique();
        builder.HasIndex(permission => permission.IsActive);
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("security_user_roles");
        builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });

        builder.Property(userRole => userRole.AssignedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(userRole => userRole.RoleId);

        builder.HasOne(userRole => userRole.Role)
            .WithMany()
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("security_role_permissions");
        builder.HasKey(rolePermission => new { rolePermission.RoleId, rolePermission.PermissionId });

        builder.Property(rolePermission => rolePermission.GrantedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasIndex(rolePermission => rolePermission.PermissionId);

        builder.HasOne(rolePermission => rolePermission.Permission)
            .WithMany()
            .HasForeignKey(rolePermission => rolePermission.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("security_audit_records");
        builder.HasKey(auditRecord => auditRecord.Id);

        builder.Property(auditRecord => auditRecord.Id).ValueGeneratedNever();
        builder.Property(auditRecord => auditRecord.Action).IsRequired();
        builder.Property(auditRecord => auditRecord.EntityType).HasMaxLength(128).IsRequired();
        builder.Property(auditRecord => auditRecord.Details).HasColumnType("text");
        builder.Property(auditRecord => auditRecord.CorrelationId).HasMaxLength(128);
        builder.Property(auditRecord => auditRecord.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();

        builder.HasIndex(auditRecord => auditRecord.OccurredAtUtc);
        builder.HasIndex(auditRecord => auditRecord.ActorUserId);
        builder.HasIndex(auditRecord => new { auditRecord.EntityType, auditRecord.EntityId });
    }
}

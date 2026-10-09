using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Identity;
using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Maps the security entities of the Phase 0 Specification (User, Permission, Role, RolePermission, RoleAssignment,
/// SodConflict) to schema "kernel", and the foreign keys from AuditEvent and IdempotencyRecord to User that the
/// step 4 brief adds (R-AUD-01). Enums as text with check constraints; the company scope convention on the
/// assignment's optional site (composite foreign key to site (id, company_id)); SodConflict pairs ordered so a pair
/// exists once (R-SEC-02).
/// </summary>
internal static class IdentityModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            Common(entity, "user_account");
            entity.Property(e => e.ExternalSubject).HasColumnName("external_subject").HasMaxLength(255);
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(320);
            entity.Property(e => e.IsServiceAccount).HasColumnName("is_service_account");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.HasIndex(e => e.ExternalSubject).IsUnique().HasDatabaseName("ux_user_account_external_subject");
            entity.ToTable(table => table.HasCheckConstraint("ck_user_account_status", "status IN ('Active', 'Disabled')"));
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            Common(entity, "permission");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(PermissionCodes.MaxLength);
            entity.Property(e => e.RiskLevel).HasColumnName("risk_level").HasMaxLength(20);
            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_permission_code");
            entity.HasAlternateKey(e => e.Code).HasName("ak_permission_code");
            entity.ToTable(table => table.HasCheckConstraint("ck_permission_risk_level", "risk_level IN ('Normal', 'Sensitive')"));
        });

        modelBuilder.Entity<Role>(entity =>
        {
            Common(entity, "role");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(40);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200);
            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_role_code");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            Common(entity, "role_permission");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.PermissionCode).HasColumnName("permission_code").HasMaxLength(PermissionCodes.MaxLength);
            entity.HasIndex(e => new { e.RoleId, e.PermissionCode }).IsUnique().HasDatabaseName("ux_role_permission_role_code");
            entity.HasOne<Role>().WithMany().HasForeignKey(e => e.RoleId).HasConstraintName("fk_role_permission_role").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Permission>().WithMany().HasForeignKey(e => e.PermissionCode).HasPrincipalKey(p => p.Code).HasConstraintName("fk_role_permission_permission").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleAssignment>(entity =>
        {
            Common(entity, "role_assignment");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.SiteId).HasColumnName("site_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to");
            entity.Property(e => e.AssignedBy).HasColumnName("assigned_by");
            entity.HasIndex(e => new { e.UserId, e.CompanyId }).HasDatabaseName("ix_role_assignment_user_company");
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(e => e.UserId).HasConstraintName("fk_role_assignment_user").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Role>().WithMany().HasForeignKey(e => e.RoleId).HasConstraintName("fk_role_assignment_role").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_role_assignment_company").OnDelete(DeleteBehavior.Restrict);
            // Company scope: a site that narrows the assignment belongs to the assignment's company (Conventions, Company scope).
            entity.HasOne<Site>().WithMany().HasForeignKey(e => new { e.SiteId, e.CompanyId }).HasPrincipalKey(s => new { s.Id, s.CompanyId })
                .HasConstraintName("fk_role_assignment_site_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(e => e.AssignedBy).HasConstraintName("fk_role_assignment_assigned_by").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("ck_role_assignment_validity", "valid_to IS NULL OR valid_to > valid_from"));
        });

        modelBuilder.Entity<SodConflict>(entity =>
        {
            Common(entity, "sod_conflict");
            entity.Property(e => e.PermissionA).HasColumnName("permission_a").HasMaxLength(PermissionCodes.MaxLength);
            entity.Property(e => e.PermissionB).HasColumnName("permission_b").HasMaxLength(PermissionCodes.MaxLength);
            entity.HasIndex(e => new { e.PermissionA, e.PermissionB }).IsUnique().HasDatabaseName("ux_sod_conflict_pair");
            // R-SEC-02: the pair is stored in code order, so (a, b) and (b, a) cannot both exist.
            entity.ToTable(table => table.HasCheckConstraint("ck_sod_conflict_order", "permission_a < permission_b"));
        });

        // R-AUD-01 and the step 4 brief: every audit event and idempotency record names a User.
        modelBuilder.Entity<AuditEvent>(entity =>
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(e => e.UserId).HasConstraintName("fk_audit_event_user").OnDelete(DeleteBehavior.Restrict));
        modelBuilder.Entity<IdempotencyRecord>(entity =>
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(e => e.UserId).HasConstraintName("fk_idempotency_record_user").OnDelete(DeleteBehavior.Restrict));
    }

    private static void Common<T>(EntityTypeBuilder<T> entity, string table)
        where T : MasterDataRow
    {
        entity.ToTable(table);
        entity.HasKey(e => e.Id).HasName($"pk_{table}");
        entity.Property(e => e.Id).HasColumnName("id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        entity.Property(e => e.CreatedBy).HasColumnName("created_by");
        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        entity.Property(e => e.UpdatedBy).HasColumnName("updated_by");
        entity.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1L);
        entity.Property(e => e.Extension).HasColumnName("extension").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
    }
}

using Microsoft.EntityFrameworkCore;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Persistence.Entities.Identity;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Persistence.Entities.Settings;

namespace Themis.Kernel.Persistence;

/// <summary>
/// The settings tables (milestone 2 step 2; Phase 0 Specification, Settings entities; D-58). kernel.setting_definition
/// is written by Themis.Migrator only and read by the kernel role. kernel.setting_value is insert-only: a check ties
/// the company, site and warehouse to the level, composite foreign keys keep a site or warehouse inside its company
/// (Conventions, Company scope), and the slot's version is unique with NULLS NOT DISTINCT, so two concurrent writes
/// to one slot cannot both succeed (R-API-02). The privilege matrix gives the kernel role SELECT and INSERT and no
/// UPDATE or DELETE on values (R-SET-01, I-14).
/// </summary>
internal static class SettingsModel
{
    public const string ValueSlotIndex = "ux_setting_value_slot_version";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingDefinitionRow>(entity =>
        {
            entity.ToTable("setting_definition");
            entity.HasKey(e => e.Id).HasName("pk_setting_definition");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(SettingKeys.MaxLength);
            entity.Property(e => e.ValueType).HasColumnName("value_type").HasMaxLength(20);
            entity.Property(e => e.DefaultValue).HasColumnName("default_value");
            entity.Property(e => e.Min).HasColumnName("min").HasMaxLength(100);
            entity.Property(e => e.Max).HasColumnName("max").HasMaxLength(100);
            entity.Property(e => e.AllowedValues).HasColumnName("allowed_values");
            entity.Property(e => e.Levels).HasColumnName("levels").HasMaxLength(100);
            entity.Property(e => e.HistoryBearing).HasColumnName("history_bearing");
            entity.Property(e => e.LockedReason).HasColumnName("locked_reason");
            entity.Property(e => e.Backing).HasColumnName("backing").HasMaxLength(20);
            entity.Property(e => e.ColumnName).HasColumnName("column_name").HasMaxLength(100);
            entity.Property(e => e.Phase).HasColumnName("phase").HasMaxLength(100);
            entity.Property(e => e.PlannedLevels).HasColumnName("planned_levels").HasMaxLength(100);
            entity.Property(e => e.RuleId).HasColumnName("rule_id").HasMaxLength(20);
            entity.Property(e => e.DecisionId).HasColumnName("decision_id").HasMaxLength(20);
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Retired).HasColumnName("retired");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // The alternate key is the key's one unique constraint; the duplicate index ux_setting_definition_key is dropped (RR-95).
            entity.HasAlternateKey(e => e.Key).HasName("ak_setting_definition_key");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_setting_definition_value_type", "value_type IN ('Integer', 'Decimal', 'Boolean', 'Code', 'Duration')");
                table.HasCheckConstraint("ck_setting_definition_backing", "backing IN ('Setting', 'Column', 'Data', 'Planned')");
                // D-63, RR-99: Setting and Column definitions have a value type; Data and Planned definitions have no value
                // type, default, minimum, maximum or allowed values.
                table.HasCheckConstraint("ck_setting_definition_shape",
                    "(backing IN ('Setting', 'Column') AND value_type IS NOT NULL) OR " +
                    "(backing IN ('Data', 'Planned') AND value_type IS NULL AND default_value IS NULL AND min IS NULL AND max IS NULL AND allowed_values IS NULL)");
            });
        });

        modelBuilder.Entity<SettingValueRow>(entity =>
        {
            entity.ToTable("setting_value");
            entity.HasKey(e => e.Id).HasName("pk_setting_value");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DefinitionKey).HasColumnName("definition_key").HasMaxLength(SettingKeys.MaxLength);
            entity.Property(e => e.Level).HasColumnName("level").HasMaxLength(20);
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.SiteId).HasColumnName("site_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(20);
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            entity.Property(e => e.RecordedBy).HasColumnName("recorded_by");
            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.HasOne<SettingDefinitionRow>().WithMany().HasForeignKey(e => e.DefinitionKey).HasPrincipalKey(d => d.Key).HasConstraintName("fk_setting_value_definition").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_setting_value_company").OnDelete(DeleteBehavior.Restrict);
            // Company scope: a site or warehouse value names its company, and the composite keys prove it is that company's (Conventions, Company scope).
            entity.HasOne<Site>().WithMany().HasForeignKey(e => new { e.SiteId, e.CompanyId }).HasPrincipalKey(s => new { s.Id, s.CompanyId })
                .HasConstraintName("fk_setting_value_site_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.WarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_setting_value_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(e => e.RecordedBy).HasConstraintName("fk_setting_value_recorded_by").OnDelete(DeleteBehavior.Restrict);
            // R-API-02: one version per slot; NULLS NOT DISTINCT so the installation slot and a company slot are each one slot.
            entity.HasIndex(e => new { e.DefinitionKey, e.Level, e.CompanyId, e.SiteId, e.WarehouseId, e.Version }).IsUnique().AreNullsDistinct(false).HasDatabaseName(ValueSlotIndex);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_setting_value_level", "level IN ('Installation', 'Company', 'Site', 'Warehouse')");
                table.HasCheckConstraint("ck_setting_value_kind", "kind IN ('Set', 'Cleared')");
                table.HasCheckConstraint("ck_setting_value_value", "(kind = 'Set' AND value IS NOT NULL) OR (kind = 'Cleared' AND value IS NULL)");
                table.HasCheckConstraint("ck_setting_value_version", "version >= 1");
                // Phase 0 Specification, SettingValue: the target columns the level needs, and only those.
                table.HasCheckConstraint("ck_setting_value_target",
                    "(level = 'Installation' AND company_id IS NULL AND site_id IS NULL AND warehouse_id IS NULL) OR " +
                    "(level = 'Company' AND company_id IS NOT NULL AND site_id IS NULL AND warehouse_id IS NULL) OR " +
                    "(level = 'Site' AND company_id IS NOT NULL AND site_id IS NOT NULL AND warehouse_id IS NULL) OR " +
                    "(level = 'Warehouse' AND company_id IS NOT NULL AND site_id IS NULL AND warehouse_id IS NOT NULL)");
            });
        });
    }
}

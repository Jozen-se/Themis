using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Themis.Kernel.Persistence.Entities.MasterData;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Persistence.Entities.Posting;
using Themis.Kernel.Persistence.Privileges;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Maps the general ledger core of milestone 1 step 6 (Phase 0 Specification: ChartOfAccounts and Account; Posting
/// profile) to schema "kernel": account with its number unique per chart (R-MD-05) and an alternate key on
/// (id, is_control); posting_profile_rule with the constraints generated from the account role catalogue (the role
/// list, R-PP-06, and the stored expects_control column whose composite foreign key to the account enforces
/// R-PP-04), the enum checks of the fixed keys, and the item posting group reference by code as ItemCompany has it.
/// The no-overlap exclusion constraint (R-MD-02) is raw SQL in the migration, since the model cannot express it.
/// </summary>
internal static class GeneralLedgerModel
{
    private const int CodeLength = 40;
    private const int NameLength = 200;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            Common(entity, "account");
            entity.Property(e => e.ChartId).HasColumnName("chart_id");
            entity.Property(e => e.AccountNumber).HasColumnName("account_number").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.AccountType).HasColumnName("account_type").HasMaxLength(20);
            entity.Property(e => e.IsControl).HasColumnName("is_control");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.HasIndex(e => new { e.ChartId, e.AccountNumber }).IsUnique().HasDatabaseName("ux_account_chart_number");
            // R-PP-04: the posting profile rule references (id, is_control), so a rule's account cannot change its control flag.
            entity.HasAlternateKey(e => new { e.Id, e.IsControl }).HasName("ak_account_id_is_control");
            entity.HasOne<ChartOfAccounts>().WithMany().HasForeignKey(e => e.ChartId).HasConstraintName("fk_account_chart").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_account_account_type", "account_type IN ('Asset', 'Liability', 'Equity', 'Revenue', 'Expense')");
                table.HasCheckConstraint("ck_account_status", "status IN ('Active', 'Blocked', 'Inactive')");
            });
        });

        modelBuilder.Entity<PostingProfileRule>(entity =>
        {
            Common(entity, "posting_profile_rule");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.AccountRole).HasColumnName("account_role").HasMaxLength(CodeLength);
            entity.Property(e => e.ItemPostingGroup).HasColumnName("item_posting_group").HasMaxLength(CodeLength);
            entity.Property(e => e.PartyPostingGroup).HasColumnName("party_posting_group").HasMaxLength(20);
            entity.Property(e => e.ShipToScope).HasColumnName("ship_to_scope").HasMaxLength(20);
            entity.Property(e => e.TaxCodeId).HasColumnName("tax_code_id");
            entity.Property(e => e.ReasonCode).HasColumnName("reason_code").HasMaxLength(20);
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            // R-PP-04: generated from the account role catalogue; the foreign key below checks it against the account.
            entity.Property(e => e.ExpectsControl).HasColumnName("expects_control").HasComputedColumnSql(AccountRoleConstraints.ExpectsControlSql(), stored: true);
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to");
            entity.HasIndex(e => new { e.CompanyId, e.AccountRole }).HasDatabaseName("ix_posting_profile_rule_company_role");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_posting_profile_rule_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ItemPostingGroup>().WithMany().HasForeignKey(e => e.ItemPostingGroup).HasPrincipalKey(g => g.Code).HasConstraintName("fk_posting_profile_rule_item_posting_group").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Account>().WithMany().HasForeignKey(e => new { e.AccountId, e.ExpectsControl }).HasPrincipalKey(a => new { a.Id, a.IsControl })
                .HasConstraintName("fk_posting_profile_rule_account_control").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_posting_profile_rule_account_role", AccountRoleConstraints.RoleListSql());
                table.HasCheckConstraint("ck_posting_profile_rule_keys_by_role", AccountRoleConstraints.KeysByRoleSql());
                table.HasCheckConstraint("ck_posting_profile_rule_party_posting_group", "party_posting_group IS NULL OR party_posting_group IN ('Domestic', 'EU', 'NonEU')");
                table.HasCheckConstraint("ck_posting_profile_rule_ship_to_scope", "ship_to_scope IS NULL OR ship_to_scope IN ('Domestic', 'EU', 'NonEU')");
                table.HasCheckConstraint("ck_posting_profile_rule_reason_code", "reason_code IS NULL OR reason_code IN ('OpeningBalance', 'CountDifference', 'Scrap', 'Other')");
                table.HasCheckConstraint("ck_posting_profile_rule_validity", "valid_to IS NULL OR valid_to > valid_from");
            });
        });
    }

    /// <summary>R-MD-02 with the Specification's reading for empty keys: a blank key is coalesced to a sentinel inside the constraint, so two rules that leave the same key empty overlap; the columns stay NULL.</summary>
    public const string NoOverlapConstraintSql =
        "ALTER TABLE kernel.posting_profile_rule ADD CONSTRAINT ex_posting_profile_rule_no_overlap EXCLUDE USING gist (" +
        "company_id WITH =, account_role WITH =, coalesce(item_posting_group, '') WITH =, coalesce(party_posting_group, '') WITH =, " +
        "coalesce(ship_to_scope, '') WITH =, coalesce(tax_code_id, '00000000-0000-0000-0000-000000000000'::uuid) WITH =, coalesce(reason_code, '') WITH =, " +
        "daterange(valid_from, valid_to, '[)') WITH &&);";

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

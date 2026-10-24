using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Maps the organisation entities of the Phase 0 Specification (Installation, Party, ChartOfAccounts, Company,
/// Site, Warehouse, FiscalYear, FiscalPeriod) to schema "kernel": snake_case names, enums as text with check
/// constraints, codes unique within the scope their attribute names (R-MD-05), and the company scope convention
/// enforced by composite foreign keys where the company is derived from a parent.
/// </summary>
internal static class OrganisationModel
{
    private const int CodeLength = 40;
    private const int NameLength = 200;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Installation>(entity =>
        {
            Common(entity, "installation");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.DefaultLocale).HasColumnName("default_locale").HasMaxLength(35);
            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_installation_code");
            entity.ToTable(table => table.HasCheckConstraint("ck_installation_status", "status IN ('Active', 'Suspended')"));
        });

        modelBuilder.Entity<Party>(entity =>
        {
            Common(entity, "party");
            entity.Property(e => e.PartyNumber).HasColumnName("party_number").HasMaxLength(CodeLength);
            entity.Property(e => e.PartyType).HasColumnName("party_type").HasMaxLength(20);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.OrganisationNumber).HasColumnName("organisation_number").HasMaxLength(50);
            entity.Property(e => e.VatNumber).HasColumnName("vat_number").HasMaxLength(50);
            entity.Property(e => e.CountryCode).HasColumnName("country_code").HasMaxLength(2);
            entity.Property(e => e.Language).HasColumnName("language").HasMaxLength(35);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.DuplicateKey).HasColumnName("duplicate_key").HasMaxLength(400);
            entity.Property(e => e.AnonymisedAt).HasColumnName("anonymised_at");
            entity.HasIndex(e => e.PartyNumber).IsUnique().HasDatabaseName("ux_party_party_number");
            entity.HasIndex(e => e.DuplicateKey).HasDatabaseName("ix_party_duplicate_key");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_party_party_type", "party_type IN ('Organisation', 'Person')");
                table.HasCheckConstraint("ck_party_status", "status IN ('Active', 'Blocked', 'Inactive')");
            });
        });

        modelBuilder.Entity<ChartOfAccounts>(entity =>
        {
            Common(entity, "chart_of_accounts");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.Template).HasColumnName("template").HasMaxLength(NameLength);
            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_chart_of_accounts_code");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            Common(entity, "company");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.PartyId).HasColumnName("party_id");
            entity.Property(e => e.LegalName).HasColumnName("legal_name").HasMaxLength(NameLength);
            entity.Property(e => e.OrganisationNumber).HasColumnName("organisation_number").HasMaxLength(50);
            entity.Property(e => e.OrganisationNumberNormalised).HasColumnName("organisation_number_normalised").HasMaxLength(50);
            entity.Property(e => e.VatNumber).HasColumnName("vat_number").HasMaxLength(50);
            entity.Property(e => e.CountryCode).HasColumnName("country_code").HasMaxLength(2);
            entity.Property(e => e.BaseCurrency).HasColumnName("base_currency").HasMaxLength(3);
            entity.Property(e => e.TimeZone).HasColumnName("time_zone").HasMaxLength(64);
            entity.Property(e => e.FiscalYearStartMonth).HasColumnName("fiscal_year_start_month");
            entity.Property(e => e.ChartOfAccountsId).HasColumnName("chart_of_accounts_id");
            entity.Property(e => e.RoundingMode).HasColumnName("rounding_mode").HasMaxLength(20);
            entity.Property(e => e.TaxRoundingLevel).HasColumnName("tax_rounding_level").HasMaxLength(20);
            entity.Property(e => e.PaymentTolerance).HasColumnName("payment_tolerance").HasColumnType("numeric(20,4)");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.HasIndex(e => e.Code).IsUnique().HasDatabaseName("ux_company_code");
            // D-64: a party is the own party of at most one company; two companies never share an organisation number in one country (R-MD-05).
            entity.HasIndex(e => e.PartyId).IsUnique().HasDatabaseName("ux_company_party");
            entity.HasIndex(e => new { e.CountryCode, e.OrganisationNumberNormalised }).IsUnique().HasDatabaseName("ux_company_country_organisation_number");
            entity.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId).HasConstraintName("fk_company_party").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChartOfAccounts>().WithMany().HasForeignKey(e => e.ChartOfAccountsId).HasConstraintName("fk_company_chart_of_accounts").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_company_status", "status IN ('Active', 'Inactive')");
                table.HasCheckConstraint("ck_company_rounding_mode", "rounding_mode IN ('HalfAwayFromZero', 'HalfEven')");
                table.HasCheckConstraint("ck_company_tax_rounding_level", "tax_rounding_level IN ('Line', 'Document')");
                table.HasCheckConstraint("ck_company_fiscal_year_start_month", "fiscal_year_start_month BETWEEN 1 AND 12");
            });
        });

        modelBuilder.Entity<Site>(entity =>
        {
            Common(entity, "site");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.AddressId).HasColumnName("address_id");
            entity.Property(e => e.TimeZone).HasColumnName("time_zone").HasMaxLength(64);
            entity.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasDatabaseName("ux_site_company_code");
            // Company scope: (id, company_id) is the target of the warehouse's composite foreign key.
            entity.HasAlternateKey(e => new { e.Id, e.CompanyId }).HasName("ak_site_id_company_id");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_site_company").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Warehouse>(entity =>
        {
            Common(entity, "warehouse");
            entity.Property(e => e.SiteId).HasColumnName("site_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasDatabaseName("ux_warehouse_company_code");
            entity.HasIndex(e => e.SiteId).IsUnique().HasDatabaseName("ux_warehouse_site_default").HasFilter("is_default");
            // Company scope: the stored company must be the site's company (Conventions, Company scope).
            entity.HasOne<Site>().WithMany().HasForeignKey(e => new { e.SiteId, e.CompanyId }).HasPrincipalKey(s => new { s.Id, s.CompanyId })
                .HasConstraintName("fk_warehouse_site_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("ck_warehouse_status", "status IN ('Active', 'Blocked', 'Inactive')"));
        });

        modelBuilder.Entity<FiscalYear>(entity =>
        {
            Common(entity, "fiscal_year");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasDatabaseName("ux_fiscal_year_company_code");
            entity.HasAlternateKey(e => new { e.Id, e.CompanyId }).HasName("ak_fiscal_year_id_company_id");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_fiscal_year_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_fiscal_year_status", "status IN ('Open', 'Closed')");
                table.HasCheckConstraint("ck_fiscal_year_dates", "start_date <= end_date");
            });
        });

        modelBuilder.Entity<FiscalPeriod>(entity =>
        {
            Common(entity, "fiscal_period");
            entity.Property(e => e.FiscalYearId).HasColumnName("fiscal_year_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.PeriodNumber).HasColumnName("period_number");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.HasIndex(e => new { e.FiscalYearId, e.PeriodNumber }).IsUnique().HasDatabaseName("ux_fiscal_period_year_number");
            entity.HasIndex(e => new { e.CompanyId, e.StartDate }).HasDatabaseName("ix_fiscal_period_company_start");
            // Company scope: the stored company must be the fiscal year's company (Conventions, Company scope).
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(e => new { e.FiscalYearId, e.CompanyId }).HasPrincipalKey(y => new { y.Id, y.CompanyId })
                .HasConstraintName("fk_fiscal_period_year_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_fiscal_period_status", "status IN ('Open', 'SoftClosed', 'Closed')");
                table.HasCheckConstraint("ck_fiscal_period_number", "period_number BETWEEN 1 AND 18");
                table.HasCheckConstraint("ck_fiscal_period_dates", "start_date <= end_date");
            });
        });
    }

    /// <summary>The common fields of every master data entity (Conventions).</summary>
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

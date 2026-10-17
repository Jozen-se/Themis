using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Themis.Kernel.Persistence.Entities.MasterData;
using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Maps the master data entities of the Phase 0 Specification (UnitOfMeasure, Currency, ItemPostingGroup, Item,
/// ItemCompany) to schema "kernel": snake_case names, enums as text with check constraints, codes unique within
/// their scope (R-MD-05), and the references by code the step 5 brief adds: Item.base_unit to UnitOfMeasure,
/// Company.base_currency to Currency, ItemCompany.item_posting_group to ItemPostingGroup. The warehouse reference
/// carries the company (composite key), so a default warehouse always belongs to the row's company.
/// </summary>
internal static class MasterDataModel
{
    private const int CodeLength = 40;
    private const int NameLength = 200;

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UnitOfMeasure>(entity =>
        {
            Common(entity, "unit_of_measure");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.Dimension).HasColumnName("dimension").HasMaxLength(20);
            entity.Property(e => e.Decimals).HasColumnName("decimals");
            entity.HasAlternateKey(e => e.Code).HasName("ak_unit_of_measure_code");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_unit_of_measure_dimension", "dimension IN ('Count', 'Mass', 'Length', 'Volume', 'Time')");
                table.HasCheckConstraint("ck_unit_of_measure_decimals", "decimals BETWEEN 0 AND 6");
            });
        });

        modelBuilder.Entity<Currency>(entity =>
        {
            Common(entity, "currency");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(3);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.Property(e => e.MinorUnits).HasColumnName("minor_units");
            entity.HasAlternateKey(e => e.Code).HasName("ak_currency_code");
            entity.ToTable(table => table.HasCheckConstraint("ck_currency_minor_units", "minor_units BETWEEN 0 AND 3"));
        });

        modelBuilder.Entity<ItemPostingGroup>(entity =>
        {
            Common(entity, "item_posting_group");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(NameLength);
            entity.HasAlternateKey(e => e.Code).HasName("ak_item_posting_group_code");
        });

        modelBuilder.Entity<Item>(entity =>
        {
            Common(entity, "item");
            entity.Property(e => e.ItemNumber).HasColumnName("item_number").HasMaxLength(CodeLength);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(NameLength);
            entity.Property(e => e.ItemType).HasColumnName("item_type").HasMaxLength(20);
            entity.Property(e => e.BaseUnit).HasColumnName("base_unit").HasMaxLength(CodeLength);
            entity.Property(e => e.LotTracking).HasColumnName("lot_tracking").HasMaxLength(20);
            entity.Property(e => e.AttributeTemplateId).HasColumnName("attribute_template_id");
            entity.Property(e => e.NetWeightKg).HasColumnName("net_weight_kg").HasColumnType("numeric(20,6)");
            entity.Property(e => e.SalesMultiple).HasColumnName("sales_multiple").HasColumnType("numeric(20,6)");
            entity.Property(e => e.MinOrderQty).HasColumnName("min_order_qty").HasColumnType("numeric(20,6)");
            // R-MD-05: the item number is unique per installation, whatever the status of its company rows.
            entity.HasIndex(e => e.ItemNumber).IsUnique().HasDatabaseName("ux_item_item_number");
            entity.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(e => e.BaseUnit).HasPrincipalKey(u => u.Code).HasConstraintName("fk_item_base_unit").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_item_item_type", "item_type IN ('Stocked', 'NonStocked', 'Service')");
                table.HasCheckConstraint("ck_item_lot_tracking", "lot_tracking IN ('None', 'Required')");
                table.HasCheckConstraint("ck_item_net_weight_kg", "net_weight_kg IS NULL OR net_weight_kg >= 0");
                table.HasCheckConstraint("ck_item_sales_multiple", "sales_multiple IS NULL OR sales_multiple > 0");
                table.HasCheckConstraint("ck_item_min_order_qty", "min_order_qty IS NULL OR min_order_qty > 0");
            });
        });

        modelBuilder.Entity<ItemCompany>(entity =>
        {
            Common(entity, "item_company");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.ItemPostingGroup).HasColumnName("item_posting_group").HasMaxLength(CodeLength);
            entity.Property(e => e.TaxClass).HasColumnName("tax_class").HasMaxLength(20);
            entity.Property(e => e.CostingMethod).HasColumnName("costing_method").HasMaxLength(20);
            entity.Property(e => e.SalesAllowed).HasColumnName("sales_allowed");
            entity.Property(e => e.PurchaseAllowed).HasColumnName("purchase_allowed");
            entity.Property(e => e.DefaultWarehouseId).HasColumnName("default_warehouse_id");
            entity.Property(e => e.DefaultSalesUnit).HasColumnName("default_sales_unit").HasMaxLength(CodeLength);
            entity.Property(e => e.DefaultPurchaseUnit).HasColumnName("default_purchase_unit").HasMaxLength(CodeLength);
            // R-MD-05: one row per item and company; since step 7 an alternate key, the target of document_line's item reference (DocumentModel).
            entity.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).HasConstraintName("fk_item_company_item").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_item_company_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ItemPostingGroup>().WithMany().HasForeignKey(e => e.ItemPostingGroup).HasPrincipalKey(g => g.Code).HasConstraintName("fk_item_company_item_posting_group").OnDelete(DeleteBehavior.Restrict);
            // Company scope: the default warehouse must belong to the row's company (Specification, ItemCompany).
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.DefaultWarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_item_company_default_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_item_company_status", "status IN ('Active', 'Blocked', 'Inactive')");
                table.HasCheckConstraint("ck_item_company_tax_class", "tax_class IN ('Standard', 'Reduced', 'Zero', 'Exempt')");
                table.HasCheckConstraint("ck_item_company_costing_method", "costing_method IN ('MovingAverage')");
            });
        });

        // Step 5 brief: the company's base currency is a required reference to Currency by code.
        modelBuilder.Entity<Company>(entity =>
            entity.HasOne<Currency>().WithMany().HasForeignKey(e => e.BaseCurrency).HasPrincipalKey(c => c.Code).HasConstraintName("fk_company_base_currency").OnDelete(DeleteBehavior.Restrict));
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

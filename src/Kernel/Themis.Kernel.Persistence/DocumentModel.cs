using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Themis.Kernel.Persistence.Entities.Documents;
using Themis.Kernel.Persistence.Entities.MasterData;
using Themis.Kernel.Persistence.Entities.Organisation;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Maps the document framework of milestone 1 step 7 (Phase 0 Specification, Document framework entities; D-39,
/// D-40) to schema "kernel": the registry tables document_type and document_transition (system-owned, SELECT only
/// for the kernel role), document and document_line with their company scope enforced by composite foreign keys
/// (warehouse, the line's document, the line's item through its ItemCompany row, D-43), the INVENTORY_ADJUSTMENT
/// extension, and number_series with the Specification's rules as constraints: the fiscal year exactly when the
/// scope is PerFiscalYear and of the series' company, one series per company, type and fiscal year, gapless where
/// the type requires it (through the stored type_requires_gapless and its composite foreign key to the registry),
/// JOURNAL_ENTRY series always gapless and PerFiscalYear.
/// </summary>
internal static class DocumentModel
{
    private const int CodeLength = 40;
    private const int NameLength = 200;

    /// <summary>Numbering is the one place a document type's gapless flag is read in the database, so the registry keeps the pair as an alternate key.</summary>
    public const string DocumentTypeGaplessKey = "ak_document_type_code_gapless";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentType>(entity =>
        {
            Common(entity, "document_type");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(CodeLength);
            entity.Property(e => e.NumberAssigned).HasColumnName("number_assigned").HasMaxLength(20);
            entity.Property(e => e.Gapless).HasColumnName("gapless");
            entity.Property(e => e.PostsInventory).HasColumnName("posts_inventory");
            entity.Property(e => e.PostsGeneralLedger).HasColumnName("posts_general_ledger");
            entity.Property(e => e.PartyRole).HasColumnName("party_role").HasMaxLength(CodeLength);
            entity.HasAlternateKey(e => e.Code).HasName("ak_document_type_code");
            entity.HasAlternateKey(e => new { e.Code, e.Gapless }).HasName(DocumentTypeGaplessKey);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_document_type_number_assigned", "number_assigned IN ('OnCreate', 'OnPost', 'NumberingOnly')");
                // Specification, DocumentType: a numbering-only row has no documents, so it posts nothing and names no party role.
                table.HasCheckConstraint("ck_document_type_numbering_only", "number_assigned <> 'NumberingOnly' OR (posts_inventory = false AND posts_general_ledger = false AND party_role IS NULL)");
            });
        });

        modelBuilder.Entity<DocumentTransition>(entity =>
        {
            Common(entity, "document_transition");
            entity.Property(e => e.DocumentType).HasColumnName("document_type").HasMaxLength(CodeLength);
            entity.Property(e => e.Command).HasColumnName("command").HasMaxLength(CodeLength);
            entity.Property(e => e.FromStatus).HasColumnName("from_status").HasMaxLength(20);
            entity.Property(e => e.ToStatus).HasColumnName("to_status").HasMaxLength(20);
            entity.Property(e => e.EventName).HasColumnName("event_name").HasMaxLength(81);
            // R-DOC-01: one row per type, command and from-status; the engine allows only what is listed.
            entity.HasIndex(e => new { e.DocumentType, e.Command, e.FromStatus }).IsUnique().HasDatabaseName("ux_document_transition_type_command_from");
            entity.HasOne<DocumentType>().WithMany().HasForeignKey(e => e.DocumentType).HasPrincipalKey(t => t.Code).HasConstraintName("fk_document_transition_type").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_document_transition_from_status", "from_status IN ('Draft', 'Posted', 'Discarded', 'Reversed')");
                table.HasCheckConstraint("ck_document_transition_to_status", "to_status IN ('Draft', 'Posted', 'Discarded', 'Reversed')");
            });
        });

        modelBuilder.Entity<Document>(entity =>
        {
            Common(entity, "document");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.DocumentType).HasColumnName("document_type").HasMaxLength(CodeLength);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.DocumentNumber).HasColumnName("document_number").HasMaxLength(CodeLength);
            entity.Property(e => e.DraftReference).HasColumnName("draft_reference").HasMaxLength(CodeLength);
            entity.Property(e => e.DocumentDate).HasColumnName("document_date");
            entity.Property(e => e.PostingDate).HasColumnName("posting_date");
            entity.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.ExternalReference).HasColumnName("external_reference").HasMaxLength(NameLength);
            entity.Property(e => e.PostedAt).HasColumnName("posted_at");
            entity.Property(e => e.PostedBy).HasColumnName("posted_by");
            // Company scope: (id, company_id) is the target of the line's and the extension's composite foreign keys.
            entity.HasAlternateKey(e => new { e.Id, e.CompanyId }).HasName("ak_document_id_company_id");
            // Specification, Document: the number is unique per company and type once assigned.
            entity.HasIndex(e => new { e.CompanyId, e.DocumentType, e.DocumentNumber }).IsUnique().HasDatabaseName("ux_document_company_type_number").HasFilter("document_number IS NOT NULL");
            entity.HasIndex(e => new { e.CompanyId, e.DocumentType, e.Status }).HasDatabaseName("ix_document_company_type_status");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_document_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentType>().WithMany().HasForeignKey(e => e.DocumentType).HasPrincipalKey(t => t.Code).HasConstraintName("fk_document_type").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(e => e.Currency).HasPrincipalKey(c => c.Code).HasConstraintName("fk_document_currency").OnDelete(DeleteBehavior.Restrict);
            // Company scope: the header's default warehouse belongs to the document's company.
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.WarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_document_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("ck_document_status", "status IN ('Draft', 'Posted', 'Discarded', 'Reversed')"));
        });

        modelBuilder.Entity<DocumentLine>(entity =>
        {
            Common(entity, "document_line");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.LineNumber).HasColumnName("line_number");
            entity.Property(e => e.LineType).HasColumnName("line_type").HasMaxLength(20);
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(NameLength);
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasColumnType("numeric(20,6)");
            entity.Property(e => e.UnitCode).HasColumnName("unit_code").HasMaxLength(CodeLength);
            entity.Property(e => e.QuantityBase).HasColumnName("quantity_base").HasColumnType("numeric(20,6)");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.StockStatus).HasColumnName("stock_status").HasMaxLength(20);
            entity.Property(e => e.EnteredUnitCost).HasColumnName("entered_unit_cost").HasColumnType("numeric(28,10)");
            entity.Property(e => e.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(28,10)");
            entity.Property(e => e.CostAmount).HasColumnName("cost_amount").HasColumnType("numeric(20,4)");
            entity.Property(e => e.LineStatus).HasColumnName("line_status").HasMaxLength(20);
            entity.HasAlternateKey(e => new { e.Id, e.CompanyId }).HasName("ak_document_line_id_company_id");
            // Shared rules for every document type: line numbers are unique within the document.
            entity.HasIndex(e => new { e.DocumentId, e.LineNumber }).IsUnique().HasDatabaseName("ux_document_line_document_number");
            // Company scope: the line's company is its document's company.
            entity.HasOne<Document>().WithMany().HasForeignKey(e => new { e.DocumentId, e.CompanyId }).HasPrincipalKey(d => new { d.Id, d.CompanyId })
                .HasConstraintName("fk_document_line_document_company").OnDelete(DeleteBehavior.Restrict);
            // D-43: the item is set up in the document's company; (item_id, company_id) references its ItemCompany row.
            entity.HasOne<ItemCompany>().WithMany().HasForeignKey(e => new { e.ItemId, e.CompanyId }).HasPrincipalKey(c => new { c.ItemId, c.CompanyId })
                .HasConstraintName("fk_document_line_item_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.WarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_document_line_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(e => e.UnitCode).HasPrincipalKey(u => u.Code).HasConstraintName("fk_document_line_unit").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_document_line_line_number", "line_number > 0");
                table.HasCheckConstraint("ck_document_line_line_type", "line_type IN ('Item', 'Charge', 'Account', 'Allocation', 'Text')");
                table.HasCheckConstraint("ck_document_line_stock_status", "stock_status IS NULL OR stock_status IN ('Available', 'Blocked')");
                table.HasCheckConstraint("ck_document_line_line_status", "line_status IN ('Open', 'Closed', 'Cancelled')");
                // Specification, DocumentLine: an Item line carries its item, quantity, unit, base quantity, warehouse and stock status; a quantity is never 0 (I-02).
                table.HasCheckConstraint("ck_document_line_item_line", "line_type <> 'Item' OR (item_id IS NOT NULL AND quantity IS NOT NULL AND unit_code IS NOT NULL AND quantity_base IS NOT NULL AND warehouse_id IS NOT NULL AND stock_status IS NOT NULL)");
                table.HasCheckConstraint("ck_document_line_quantity", "quantity IS NULL OR quantity <> 0");
                table.HasCheckConstraint("ck_document_line_entered_unit_cost", "entered_unit_cost IS NULL OR entered_unit_cost >= 0");
            });
        });

        modelBuilder.Entity<InventoryAdjustment>(entity =>
        {
            Common(entity, "inventory_adjustment");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ReasonCode).HasColumnName("reason_code").HasMaxLength(20);
            // D-16: a one-to-one extension keyed by the document id; the company travels with it (Company scope convention).
            entity.HasOne<Document>().WithOne().HasForeignKey<InventoryAdjustment>(e => new { e.Id, e.CompanyId }).HasPrincipalKey<Document>(d => new { d.Id, d.CompanyId })
                .HasConstraintName("fk_inventory_adjustment_document_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table => table.HasCheckConstraint("ck_inventory_adjustment_reason_code", "reason_code IS NULL OR reason_code IN ('OpeningBalance', 'CountDifference', 'Scrap', 'Other')"));
        });

        modelBuilder.Entity<NumberSeries>(entity =>
        {
            Common(entity, "number_series");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.DocumentType).HasColumnName("document_type").HasMaxLength(CodeLength);
            entity.Property(e => e.Scope).HasColumnName("scope").HasMaxLength(20);
            entity.Property(e => e.FiscalYearId).HasColumnName("fiscal_year_id");
            entity.Property(e => e.Prefix).HasColumnName("prefix").HasMaxLength(CodeLength);
            entity.Property(e => e.Padding).HasColumnName("padding");
            entity.Property(e => e.NextNumber).HasColumnName("next_number");
            entity.Property(e => e.Gapless).HasColumnName("gapless");
            entity.Property(e => e.TypeRequiresGapless).HasColumnName("type_requires_gapless");
            // Specification, NumberSeries: one series per company, document type and fiscal year; a continuous series has none.
            entity.HasIndex(e => new { e.CompanyId, e.DocumentType, e.FiscalYearId }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_number_series_company_type_year");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_number_series_company").OnDelete(DeleteBehavior.Restrict);
            // The type's gapless flag travels in type_requires_gapless, so the check below can read it (Specification, NumberSeries: copied from the document type).
            entity.HasOne<DocumentType>().WithMany().HasForeignKey(e => new { e.DocumentType, e.TypeRequiresGapless }).HasPrincipalKey(t => new { t.Code, t.Gapless })
                .HasConstraintName("fk_number_series_type_gapless").OnDelete(DeleteBehavior.Restrict);
            // Company scope: a per-fiscal-year series names a fiscal year of its own company.
            entity.HasOne<FiscalYear>().WithMany().HasForeignKey(e => new { e.FiscalYearId, e.CompanyId }).HasPrincipalKey(y => new { y.Id, y.CompanyId })
                .HasConstraintName("fk_number_series_fiscal_year_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_number_series_scope", "scope IN ('Continuous', 'PerFiscalYear')");
                table.HasCheckConstraint("ck_number_series_fiscal_year", "(scope = 'PerFiscalYear') = (fiscal_year_id IS NOT NULL)");
                table.HasCheckConstraint("ck_number_series_gapless", "type_requires_gapless = false OR gapless = true");
                table.HasCheckConstraint("ck_number_series_journal_entry", "document_type <> 'JOURNAL_ENTRY' OR (gapless = true AND scope = 'PerFiscalYear')");
                table.HasCheckConstraint("ck_number_series_padding", "padding BETWEEN 0 AND 20");
                table.HasCheckConstraint("ck_number_series_next_number", "next_number >= 1");
            });
        });

        // D-43 and Specification, DocumentLine: the line references (item_id, company_id) of the ItemCompany row, so the
        // step 5 unique index becomes an alternate key.
        modelBuilder.Entity<ItemCompany>(entity => entity.HasAlternateKey(e => new { e.ItemId, e.CompanyId }).HasName("ak_item_company_item_company"));
    }

    /// <summary>The common fields of every entity (Conventions).</summary>
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

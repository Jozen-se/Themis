using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Themis.Kernel.Persistence.Entities.Documents;
using Themis.Kernel.Persistence.Entities.MasterData;
using Themis.Kernel.Persistence.Entities.Organisation;
using Themis.Kernel.Persistence.Entities.Posting;

namespace Themis.Kernel.Persistence;

/// <summary>
/// Maps the ledgers and caches of milestone 1 step 8 (Phase 0 Specification, Ledger entities and Caches) to schema
/// "kernel": journal_entry, journal_line and inventory_entry append-only without updated or version fields
/// (R-LED-01, R-LED-06; the kernel role holds INSERT and SELECT only, I-11), item_cost_state and stock_balance as
/// rebuildable caches (R-INV-01) with one row per key (NULLS NOT DISTINCT on the lot). Every ledger row stores its
/// company, tied by composite foreign keys to its document, line, item company row and warehouse (Company scope
/// convention, D-43); lot_id exists on the entry and the balance (part of the key, D-18) but a check keeps it NULL
/// until Lot exists. Amounts are signed debit positive and never 0 (D-15, I-02), an inventory entry's cost needs a
/// journal entry (R-INV-06), and a cost shares the sign of its quantity.
/// </summary>
internal static class LedgerModel
{
    private const int CodeLength = 40;

    /// <summary>
    /// D-51: the command id is required for every audit event written from step 8 on. The check is added NOT VALID, so
    /// it binds every row written after it and never touches the events migrations and seeders wrote before; it stays
    /// NOT VALID, since validating it would need those rows changed (R-AUD-01).
    /// </summary>
    public const string AuditCommandIdCheckSql = "ALTER TABLE kernel.audit_event ADD CONSTRAINT ck_audit_event_command_id CHECK (command_id IS NOT NULL) NOT VALID;";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JournalEntry>(entity =>
        {
            entity.ToTable("journal_entry");
            entity.HasKey(e => e.Id).HasName("pk_journal_entry");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.EntryNumber).HasColumnName("entry_number").HasMaxLength(CodeLength);
            entity.Property(e => e.FiscalPeriodId).HasColumnName("fiscal_period_id");
            entity.Property(e => e.PostingDate).HasColumnName("posting_date");
            entity.Property(e => e.DocumentDate).HasColumnName("document_date");
            entity.Property(e => e.SourceDocumentId).HasColumnName("source_document_id");
            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(200);
            entity.Property(e => e.ReversalOfEntryId).HasColumnName("reversal_of_entry_id");
            entity.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            entity.Property(e => e.RecordedBy).HasColumnName("recorded_by");
            entity.Property(e => e.Extension).HasColumnName("extension").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            // Company scope: (id, company_id) is the target of the line's composite foreign key.
            entity.HasAlternateKey(e => new { e.Id, e.CompanyId }).HasName("ak_journal_entry_id_company_id");
            // R-DOC-07, I-09: gapless per series and fiscal year; the number is unique per company.
            entity.HasIndex(e => new { e.CompanyId, e.EntryNumber }).IsUnique().HasDatabaseName("ux_journal_entry_company_number");
            entity.HasIndex(e => new { e.CompanyId, e.PostingDate }).HasDatabaseName("ix_journal_entry_company_posting_date");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_journal_entry_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FiscalPeriod>().WithMany().HasForeignKey(e => e.FiscalPeriodId).HasConstraintName("fk_journal_entry_fiscal_period").OnDelete(DeleteBehavior.Restrict);
            // R-LED-03: every entry has a source document of the same company.
            entity.HasOne<Document>().WithMany().HasForeignKey(e => new { e.SourceDocumentId, e.CompanyId }).HasPrincipalKey(d => new { d.Id, d.CompanyId })
                .HasConstraintName("fk_journal_entry_source_document_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.ReversalOfEntryId).HasConstraintName("fk_journal_entry_reversal_of").OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLine>(entity =>
        {
            entity.ToTable("journal_line");
            entity.HasKey(e => e.Id).HasName("pk_journal_line");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.JournalEntryId).HasColumnName("journal_entry_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.LineNumber).HasColumnName("line_number");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.AccountRole).HasColumnName("account_role").HasMaxLength(CodeLength);
            entity.Property(e => e.PostingRuleId).HasColumnName("posting_rule_id");
            entity.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(20,4)");
            entity.Property(e => e.TxnCurrency).HasColumnName("txn_currency").HasMaxLength(3);
            entity.Property(e => e.TxnAmount).HasColumnName("txn_amount").HasColumnType("numeric(20,4)");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.SourceLineId).HasColumnName("source_line_id");
            entity.Property(e => e.Extension).HasColumnName("extension").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            entity.HasIndex(e => new { e.JournalEntryId, e.LineNumber }).IsUnique().HasDatabaseName("ux_journal_line_entry_number");
            entity.HasIndex(e => e.AccountId).HasDatabaseName("ix_journal_line_account");
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(e => new { e.JournalEntryId, e.CompanyId }).HasPrincipalKey(j => new { j.Id, j.CompanyId })
                .HasConstraintName("fk_journal_line_entry_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).HasConstraintName("fk_journal_line_account").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PostingProfileRule>().WithMany().HasForeignKey(e => e.PostingRuleId).HasConstraintName("fk_journal_line_posting_rule").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(e => e.TxnCurrency).HasPrincipalKey(c => c.Code).HasConstraintName("fk_journal_line_txn_currency").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ItemCompany>().WithMany().HasForeignKey(e => new { e.ItemId, e.CompanyId }).HasPrincipalKey(c => new { c.ItemId, c.CompanyId })
                .HasConstraintName("fk_journal_line_item_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.WarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_journal_line_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentLine>().WithMany().HasForeignKey(e => new { e.SourceLineId, e.CompanyId }).HasPrincipalKey(l => new { l.Id, l.CompanyId })
                .HasConstraintName("fk_journal_line_source_line_company").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_journal_line_amount", "amount <> 0");
                table.HasCheckConstraint("ck_journal_line_txn_sign", "sign(txn_amount) = sign(amount)");
                table.HasCheckConstraint("ck_journal_line_line_number", "line_number > 0");
            });
        });

        modelBuilder.Entity<InventoryEntry>(entity =>
        {
            entity.ToTable("inventory_entry");
            entity.HasKey(e => e.Id).HasName("pk_inventory_entry");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.LotId).HasColumnName("lot_id");
            entity.Property(e => e.StockStatus).HasColumnName("stock_status").HasMaxLength(20);
            entity.Property(e => e.MovementType).HasColumnName("movement_type").HasMaxLength(20);
            entity.Property(e => e.QuantityBase).HasColumnName("quantity_base").HasColumnType("numeric(20,6)");
            entity.Property(e => e.CostAmount).HasColumnName("cost_amount").HasColumnType("numeric(20,4)");
            entity.Property(e => e.UnitCost).HasColumnName("unit_cost").HasColumnType("numeric(28,10)");
            entity.Property(e => e.PostingDate).HasColumnName("posting_date");
            entity.Property(e => e.DocumentDate).HasColumnName("document_date");
            entity.Property(e => e.SourceLineId).HasColumnName("source_line_id");
            entity.Property(e => e.JournalEntryId).HasColumnName("journal_entry_id");
            entity.Property(e => e.ReversalOfEntryId).HasColumnName("reversal_of_entry_id");
            entity.Property(e => e.CommandId).HasColumnName("command_id");
            entity.Property(e => e.RecordedAt).HasColumnName("recorded_at");
            entity.Property(e => e.RecordedBy).HasColumnName("recorded_by");
            entity.Property(e => e.Extension).HasColumnName("extension").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            entity.HasIndex(e => new { e.CompanyId, e.ItemId, e.WarehouseId, e.StockStatus }).HasDatabaseName("ix_inventory_entry_balance_key");
            entity.HasIndex(e => e.JournalEntryId).HasDatabaseName("ix_inventory_entry_journal_entry");
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).HasConstraintName("fk_inventory_entry_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ItemCompany>().WithMany().HasForeignKey(e => new { e.ItemId, e.CompanyId }).HasPrincipalKey(c => new { c.ItemId, c.CompanyId })
                .HasConstraintName("fk_inventory_entry_item_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.WarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_inventory_entry_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DocumentLine>().WithMany().HasForeignKey(e => new { e.SourceLineId, e.CompanyId }).HasPrincipalKey(l => new { l.Id, l.CompanyId })
                .HasConstraintName("fk_inventory_entry_source_line_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(e => new { e.JournalEntryId, e.CompanyId }).HasPrincipalKey(j => new { j.Id, j.CompanyId })
                .HasConstraintName("fk_inventory_entry_journal_entry_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryEntry>().WithMany().HasForeignKey(e => e.ReversalOfEntryId).HasConstraintName("fk_inventory_entry_reversal_of").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_inventory_entry_stock_status", "stock_status IN ('Available', 'Blocked')");
                table.HasCheckConstraint("ck_inventory_entry_movement_type", "movement_type IN ('AdjustmentIn', 'AdjustmentOut', 'Issue', 'ReturnIn', 'StatusOut', 'StatusIn')");
                // I-02: a quantity is never 0; the cost shares its sign or is 0 (I-08).
                table.HasCheckConstraint("ck_inventory_entry_quantity", "quantity_base <> 0");
                table.HasCheckConstraint("ck_inventory_entry_cost_sign", "cost_amount = 0 OR sign(cost_amount) = sign(quantity_base)");
                // R-INV-06: a cost needs its journal entry.
                table.HasCheckConstraint("ck_inventory_entry_journal_entry", "cost_amount = 0 OR journal_entry_id IS NOT NULL");
                // D-18: the lot is part of the key but waits for Lot (milestone 1).
                table.HasCheckConstraint("ck_inventory_entry_lot_id", "lot_id IS NULL");
            });
        });

        modelBuilder.Entity<ItemCostState>(entity =>
        {
            Common(entity, "item_cost_state");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.QuantityBase).HasColumnName("quantity_base").HasColumnType("numeric(20,6)");
            entity.Property(e => e.InventoryValue).HasColumnName("inventory_value").HasColumnType("numeric(20,4)");
            entity.Property(e => e.AverageCost).HasColumnName("average_cost").HasColumnType("numeric(28,10)");
            entity.Property(e => e.LastInventoryEntryId).HasColumnName("last_inventory_entry_id");
            // Caches: one row per company and item, the lock target of class 2 (R-LED-08).
            entity.HasIndex(e => new { e.CompanyId, e.ItemId }).IsUnique().HasDatabaseName("ux_item_cost_state_company_item");
            entity.HasOne<ItemCompany>().WithMany().HasForeignKey(e => new { e.ItemId, e.CompanyId }).HasPrincipalKey(c => new { c.ItemId, c.CompanyId })
                .HasConstraintName("fk_item_cost_state_item_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryEntry>().WithMany().HasForeignKey(e => e.LastInventoryEntryId).HasConstraintName("fk_item_cost_state_last_entry").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                // R-INV-07: value is 0 when quantity is 0; stock value never turns negative.
                table.HasCheckConstraint("ck_item_cost_state_quantity", "quantity_base >= 0");
                table.HasCheckConstraint("ck_item_cost_state_value", "inventory_value >= 0 AND (quantity_base <> 0 OR inventory_value = 0)");
            });
        });

        modelBuilder.Entity<StockBalance>(entity =>
        {
            Common(entity, "stock_balance");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ItemId).HasColumnName("item_id");
            entity.Property(e => e.WarehouseId).HasColumnName("warehouse_id");
            entity.Property(e => e.LotId).HasColumnName("lot_id");
            entity.Property(e => e.StockStatus).HasColumnName("stock_status").HasMaxLength(20);
            entity.Property(e => e.OnHandBase).HasColumnName("on_hand_base").HasColumnType("numeric(20,6)");
            entity.Property(e => e.ReservedBase).HasColumnName("reserved_base").HasColumnType("numeric(20,6)");
            entity.Property(e => e.LastInventoryEntryId).HasColumnName("last_inventory_entry_id");
            // Caches: one row per company, item, warehouse, lot and stock status (D-18), the lock target of class 3 (R-LED-08).
            entity.HasIndex(e => new { e.CompanyId, e.ItemId, e.WarehouseId, e.LotId, e.StockStatus }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_stock_balance_key");
            entity.HasOne<ItemCompany>().WithMany().HasForeignKey(e => new { e.ItemId, e.CompanyId }).HasPrincipalKey(c => new { c.ItemId, c.CompanyId })
                .HasConstraintName("fk_stock_balance_item_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Warehouse>().WithMany().HasForeignKey(e => new { e.WarehouseId, e.CompanyId }).HasPrincipalKey(w => new { w.Id, w.CompanyId })
                .HasConstraintName("fk_stock_balance_warehouse_company").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryEntry>().WithMany().HasForeignKey(e => e.LastInventoryEntryId).HasConstraintName("fk_stock_balance_last_entry").OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("ck_stock_balance_stock_status", "stock_status IN ('Available', 'Blocked')");
                table.HasCheckConstraint("ck_stock_balance_lot_id", "lot_id IS NULL");
                table.HasCheckConstraint("ck_stock_balance_reserved", "reserved_base >= 0");
            });
        });
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

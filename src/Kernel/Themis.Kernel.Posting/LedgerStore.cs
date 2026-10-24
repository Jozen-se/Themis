using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// The company attributes a posting uses, read under the FOR SHARE of lock class 1b (D-65): the base currency, its
/// minor units (R-MON-02) and the rounding mode (RR-74). Nothing else may change them while the share lock is held.
/// </summary>
internal sealed record CompanyTerms(string BaseCurrency, int MinorUnits, RoundingMode RoundingMode);

/// <summary>A locked ItemCostState row (lock class 2).</summary>
internal sealed record CostStateRow(Guid Id, Guid ItemId, CostState State, long Version);

/// <summary>The key of a StockBalance row (D-18); the lot waits for Lot and is always empty in milestone 1.</summary>
internal sealed record BalanceKey(Guid ItemId, Guid WarehouseId, StockStatus StockStatus);

/// <summary>A locked StockBalance row (lock class 3).</summary>
internal sealed record BalanceRow(Guid Id, BalanceKey Key, decimal OnHand, decimal Reserved, long Version);

/// <summary>A journal entry to write, with its lines (R-LED-02: the amounts sum to 0).</summary>
internal sealed record JournalEntryRow(Guid Id, string EntryNumber, Guid FiscalPeriodId, string Description, IReadOnlyList<JournalLineRow> Lines);

internal sealed record JournalLineRow(Guid Id, int LineNumber, Guid AccountId, string AccountRole, Guid PostingRuleId, decimal Amount, Guid ItemId, Guid WarehouseId, Guid SourceLineId);

/// <summary>An inventory entry to write (signed quantity and cost, R-INV-06 through the journal entry reference).</summary>
internal sealed record InventoryEntryRow(Guid Id, Guid ItemId, Guid WarehouseId, StockStatus StockStatus, MovementType MovementType, decimal QuantityBase, decimal CostAmount, decimal UnitCost, Guid SourceLineId, Guid? JournalEntryId);

/// <summary>
/// Raw SQL on the command's unit of work as themis_kernel (R-DB-01): the locks of classes 2 and 3 in the lock order
/// (R-LED-08), the inserts into the append-only ledgers (R-LED-01, R-LED-06) and the cache updates. The kernel data
/// context stays in Themis.Kernel.Persistence; this capability reaches the tables through the unit of work only
/// (ARCH-08). Nothing here updates or deletes a ledger row.
/// </summary>
internal static class LedgerStore
{
    /// <summary>
    /// Lock class 1b (D-65, Lock order): SELECT ... FOR SHARE of the company row alone, never FOR KEY SHARE, which an
    /// ordinary UPDATE passes. A COMPANY.UPDATE's versioned UPDATE of the row waits until this transaction ends; a
    /// posting that waited behind a committed update reads the row as the update left it. The currency's minor units are
    /// read in a second statement: a join inside the locking statement would be re-checked against the currency row read
    /// before the wait, so a base currency changed meanwhile would make the locked row vanish instead of being seen.
    /// </summary>
    public static async Task<CompanyTerms> LockCompanyTermsAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<(string Currency, RoundingMode Mode)> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            "SELECT base_currency, rounding_mode FROM kernel.company WHERE id = @company FOR SHARE",
            new Dictionary<string, object?> { ["company"] = companyId },
            reader => (reader.GetString(0), Enum.Parse<RoundingMode>(reader.GetString(1))),
            cancellationToken);
        if (rows.Count != 1)
        {
            throw new InvalidOperationException($"Company {companyId} has no row to lock for posting (D-65).");
        }

        (string currency, RoundingMode mode) = rows[0];
        object? minorUnits = await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT minor_units FROM kernel.currency WHERE code = @code", new Dictionary<string, object?> { ["code"] = currency }, cancellationToken);
        return minorUnits is int units ? new CompanyTerms(currency, units, mode) : throw new InvalidOperationException($"Currency {currency} of company {companyId} has no row (fk_company_base_currency).");
    }

    /// <summary>Lock class 2: insert the missing ItemCostState rows in one ordered statement, then lock them all in one SELECT ... ORDER BY item_id FOR UPDATE.</summary>
    public static async Task<IReadOnlyList<CostStateRow>> LockCostStatesAsync(IUnitOfWork unitOfWork, Guid companyId, IReadOnlyList<Guid> itemIds, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        Guid[] items = [.. itemIds.Distinct().Order()];
        Guid[] ids = [.. items.Select(_ => EntityId.New())];
        await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.item_cost_state (id, company_id, item_id, quantity_base, inventory_value, average_cost, created_at, created_by, updated_at, updated_by)
            SELECT t.id, @company, t.item_id, 0, 0, NULL, @now, @by, @now, @by
            FROM unnest(@ids, @items) AS t(id, item_id)
            ORDER BY t.item_id
            ON CONFLICT (company_id, item_id) DO NOTHING
            """,
            new Dictionary<string, object?> { ["company"] = companyId, ["ids"] = ids, ["items"] = items, ["now"] = now, ["by"] = by.Value },
            cancellationToken);
        return await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT id, item_id, quantity_base, inventory_value, average_cost, version
            FROM kernel.item_cost_state
            WHERE company_id = @company AND item_id = ANY(@items)
            ORDER BY item_id
            FOR UPDATE
            """,
            new Dictionary<string, object?> { ["company"] = companyId, ["items"] = items },
            reader => new CostStateRow(reader.GetGuid(0), reader.GetGuid(1), new CostState(reader.GetDecimal(2), reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetDecimal(4)), reader.GetInt64(5)),
            cancellationToken);
    }

    /// <summary>Lock class 3: the same for StockBalance, ordered by company, item, warehouse, lot and stock status.</summary>
    public static async Task<IReadOnlyList<BalanceRow>> LockBalancesAsync(IUnitOfWork unitOfWork, Guid companyId, IReadOnlyList<BalanceKey> keys, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        BalanceKey[] ordered = [.. keys.Distinct().OrderBy(k => k.ItemId).ThenBy(k => k.WarehouseId).ThenBy(k => k.StockStatus.ToString(), StringComparer.Ordinal)];
        Guid[] ids = [.. ordered.Select(_ => EntityId.New())];
        Guid[] items = [.. ordered.Select(k => k.ItemId)];
        Guid[] warehouses = [.. ordered.Select(k => k.WarehouseId)];
        string[] statuses = [.. ordered.Select(k => k.StockStatus.ToString())];
        Dictionary<string, object?> parameters = new() { ["company"] = companyId, ["ids"] = ids, ["items"] = items, ["warehouses"] = warehouses, ["statuses"] = statuses, ["now"] = now, ["by"] = by.Value };
        await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.stock_balance (id, company_id, item_id, warehouse_id, lot_id, stock_status, on_hand_base, reserved_base, created_at, created_by, updated_at, updated_by)
            SELECT t.id, @company, t.item_id, t.warehouse_id, NULL, t.stock_status, 0, 0, @now, @by, @now, @by
            FROM unnest(@ids, @items, @warehouses, @statuses) AS t(id, item_id, warehouse_id, stock_status)
            ORDER BY t.item_id, t.warehouse_id, t.stock_status
            ON CONFLICT (company_id, item_id, warehouse_id, lot_id, stock_status) DO NOTHING
            """,
            parameters,
            cancellationToken);
        return await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT b.id, b.item_id, b.warehouse_id, b.stock_status, b.on_hand_base, b.reserved_base, b.version
            FROM kernel.stock_balance b
            WHERE b.company_id = @company AND b.lot_id IS NULL
              AND (b.item_id, b.warehouse_id, b.stock_status) IN (SELECT * FROM unnest(@items, @warehouses, @statuses))
            ORDER BY b.company_id, b.item_id, b.warehouse_id, b.lot_id, b.stock_status
            FOR UPDATE
            """,
            parameters,
            reader => new BalanceRow(reader.GetGuid(0), new BalanceKey(reader.GetGuid(1), reader.GetGuid(2), Enum.Parse<StockStatus>(reader.GetString(3))), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetInt64(6)),
            cancellationToken);
    }

    public static async Task InsertJournalEntryAsync(IUnitOfWork unitOfWork, InventoryAdjustmentPosting posting, JournalEntryRow entry, Guid commandId, DateTimeOffset recordedAt, UserId recordedBy, CancellationToken cancellationToken)
    {
        await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.journal_entry (id, company_id, entry_number, fiscal_period_id, posting_date, document_date, source_document_id, command_id, description, recorded_at, recorded_by)
            VALUES (@id, @company, @number, @period, @postingDate, @documentDate, @document, @command, @description, @at, @by)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = entry.Id,
                ["company"] = posting.CompanyId,
                ["number"] = entry.EntryNumber,
                ["period"] = entry.FiscalPeriodId,
                ["postingDate"] = posting.PostingDate.Value,
                ["documentDate"] = posting.DocumentDate.Value,
                ["document"] = posting.DocumentId,
                ["command"] = commandId,
                ["description"] = entry.Description,
                ["at"] = recordedAt,
                ["by"] = recordedBy.Value,
            },
            cancellationToken);
        foreach (JournalLineRow line in entry.Lines)
        {
            // D-15: amounts signed debit positive; the transaction currency is the company currency in milestone 1 (R-MON-03).
            await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
                """
                INSERT INTO kernel.journal_line (id, journal_entry_id, company_id, line_number, account_id, account_role, posting_rule_id, amount, txn_currency, txn_amount, item_id, warehouse_id, source_line_id)
                VALUES (@id, @entry, @company, @number, @account, @role, @rule, @amount, @currency, @amount, @item, @warehouse, @line)
                """,
                new Dictionary<string, object?>
                {
                    ["id"] = line.Id,
                    ["entry"] = entry.Id,
                    ["company"] = posting.CompanyId,
                    ["number"] = line.LineNumber,
                    ["account"] = line.AccountId,
                    ["role"] = line.AccountRole,
                    ["rule"] = line.PostingRuleId,
                    ["amount"] = line.Amount,
                    ["currency"] = posting.Currency.Value,
                    ["item"] = line.ItemId,
                    ["warehouse"] = line.WarehouseId,
                    ["line"] = line.SourceLineId,
                },
                cancellationToken);
        }
    }

    public static Task InsertInventoryEntryAsync(IUnitOfWork unitOfWork, InventoryAdjustmentPosting posting, InventoryEntryRow entry, Guid commandId, DateTimeOffset recordedAt, UserId recordedBy, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.inventory_entry (id, company_id, item_id, warehouse_id, lot_id, stock_status, movement_type, quantity_base, cost_amount, unit_cost, posting_date, document_date, source_line_id, journal_entry_id, command_id, recorded_at, recorded_by)
            VALUES (@id, @company, @item, @warehouse, NULL, @status, @movement, @quantity, @cost, @unitCost, @postingDate, @documentDate, @line, @journal, @command, @at, @by)
            """,
            new Dictionary<string, object?>
            {
                ["id"] = entry.Id,
                ["company"] = posting.CompanyId,
                ["item"] = entry.ItemId,
                ["warehouse"] = entry.WarehouseId,
                ["status"] = entry.StockStatus.ToString(),
                ["movement"] = entry.MovementType.ToString(),
                ["quantity"] = entry.QuantityBase,
                ["cost"] = entry.CostAmount,
                ["unitCost"] = entry.UnitCost,
                ["postingDate"] = posting.PostingDate.Value,
                ["documentDate"] = posting.DocumentDate.Value,
                ["line"] = entry.SourceLineId,
                ["journal"] = entry.JournalEntryId,
                ["command"] = commandId,
                ["at"] = recordedAt,
                ["by"] = recordedBy.Value,
            },
            cancellationToken);

    public static Task UpdateCostStateAsync(IUnitOfWork unitOfWork, CostStateRow row, CostState state, Guid lastEntryId, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.item_cost_state SET quantity_base = @quantity, inventory_value = @value, average_cost = @average, last_inventory_entry_id = @entry, updated_at = @now, updated_by = @by, version = version + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = row.Id, ["quantity"] = state.Quantity, ["value"] = state.Value, ["average"] = state.AverageCost, ["entry"] = lastEntryId, ["now"] = now, ["by"] = by.Value },
            cancellationToken);

    public static Task UpdateBalanceAsync(IUnitOfWork unitOfWork, BalanceRow row, decimal onHand, Guid lastEntryId, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.stock_balance SET on_hand_base = @onHand, last_inventory_entry_id = @entry, updated_at = @now, updated_by = @by, version = version + 1 WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = row.Id, ["onHand"] = onHand, ["entry"] = lastEntryId, ["now"] = now, ["by"] = by.Value },
            cancellationToken);
}

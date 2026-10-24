using System.Data.Common;
using System.Globalization;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// The kernel posting service for INVENTORY_ADJUSTMENT.POST (R-LED-05, R-LED-07; step 8 brief): after the document
/// framework ran the guards and locked the document row (class 1), it reads the company row FOR SHARE (class 1b,
/// D-65), taking the base currency, its minor units and the rounding mode from that locked read and rejecting a
/// document whose currency is not the base currency, then takes the locks of classes 2 and 3 in the lock order
/// (R-LED-08), so every stock and cost check runs on locked rows; costs the Open lines in line order with the moving
/// average algorithm in the company's rounding mode (RR-74, R-MON-02), carrying the cost state and stock from line
/// to line (R-INV-05, R-INV-07, R-INV-09);
/// refuses an outbound line that would take its balance key below 0, Available stock less its reservations
/// (R-INV-03, R-INV-04); resolves both accounts of every line with a non-zero cost (R-PP-01, R-PP-02, D-49) and
/// builds the one journal entry of the document in memory (E-01, E-02; D-15); takes the numbers as the last lock
/// class, the journal entry series only when a journal entry is written (R-DOC-07, R-DOC-08, D-52); then writes the
/// journal entry with its lines, the inventory entries (one per Open line, referencing the entry when the cost is
/// not 0, R-INV-06; a zero-cost line writes no journal lines, I-02, I-08) and the cache updates. Ledger rows carry
/// the command id, time and user (D-51). Nothing on a ledger table is updated afterwards (R-LED-06).
/// </summary>
internal sealed class InventoryPostingService : IPostingService
{
    public const string StockInsufficient = "stock_insufficient";
    public const string SeriesMissing = "number_series_missing";
    public const string JournalDescription = "Inventory adjustment";
    public const string AmountOutOfRange = "amount_out_of_range";
    public const string CurrencyNotBase = "document_currency_not_base_currency";

    private const string NumericValueOutOfRange = "22003";

    private readonly IAccountResolver _accounts;
    private readonly INumberSeriesService _numbers;
    private readonly IPostingHook _hook;

    public InventoryPostingService(IAccountResolver accounts, INumberSeriesService numbers, IPostingHook hook)
    {
        _accounts = accounts;
        _numbers = numbers;
        _hook = hook;
    }

    public async Task<Result<PostingOutcome>> PostInventoryAdjustmentAsync(InventoryAdjustmentPosting posting, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(posting);
        ArgumentNullException.ThrowIfNull(context);
        Result<PostingOutcome> outcome;
        try
        {
            outcome = await PostCoreAsync(posting, context, cancellationToken);
        }
        catch (LedgerOverflow)
        {
            // RR-83: an amount that overflows its ledger or cache column is the posting's rejection under R-DOC-01 (the
            // Specification's shared rule for guards without their own rule). The statement failed, so the transaction is
            // aborted: no role reset here (RR-62), and the dispatcher's rollback to its savepoint clears the state.
            return Error.Rule("R-DOC-01", AmountOutOfRange, "An amount computed by the posting exceeds the range of the column that stores it.");
        }

        // RR-62: hands the connection back as themis_app on successful return.
        await KernelServiceRole.ResetAsync(context.UnitOfWork, cancellationToken);
        return outcome;
    }

    private async Task<Result<PostingOutcome>> PostCoreAsync(InventoryAdjustmentPosting posting, ICommandContext context, CancellationToken cancellationToken)
    {
        IUnitOfWork unitOfWork = context.UnitOfWork;
        DateTimeOffset now = context.Now;
        UserId by = context.Principal.UserId;
        Guid commandId = context.CommandId.Value;
        IReadOnlyList<PostingLine> lines = [.. posting.Lines.OrderBy(line => line.LineNumber)];

        // R-LED-08, class 1b (D-65): the company row FOR SHARE, after the document row and before ItemCostState; the base
        // currency and the rounding mode come from that locked row and the minor units from the currency row read right
        // after it, never from an earlier read, so a COMPANY.UPDATE of them cannot cross this posting: its versioned
        // UPDATE waits behind the share lock, and its R-MD-06 checks then see these entries.
        CompanyTerms terms = await LedgerStore.LockCompanyTermsAsync(unitOfWork, posting.CompanyId, cancellationToken);
        if (terms.BaseCurrency != posting.Currency.Value)
        {
            // Specification, INVENTORY_ADJUSTMENT POST: a guard without a rule of its own, under R-DOC-01 with a code naming it.
            return Error.Rule("R-DOC-01", CurrencyNotBase, $"The document's currency {posting.Currency} is not the company's base currency {terms.BaseCurrency}; discard the draft and create it again.");
        }

        // R-LED-08, classes 2 and 3: every row the lines touch, inserted when missing and locked in key order, before any check reads it.
        Dictionary<Guid, CostStateRow> costStates = (await LedgerStore.LockCostStatesAsync(unitOfWork, posting.CompanyId, [.. lines.Select(line => line.ItemId)], now, by, cancellationToken)).ToDictionary(row => row.ItemId);
        Dictionary<BalanceKey, BalanceRow> balances = (await LedgerStore.LockBalancesAsync(unitOfWork, posting.CompanyId, [.. lines.Select(line => new BalanceKey(line.ItemId, line.WarehouseId, line.StockStatus))], now, by, cancellationToken)).ToDictionary(row => row.Key);
        Dictionary<Guid, CostState> state = costStates.ToDictionary(pair => pair.Key, pair => pair.Value.State);
        Dictionary<BalanceKey, decimal> onHand = balances.ToDictionary(pair => pair.Key, pair => pair.Value.OnHand);

        // One pass over the Open lines in line order, carrying the cost state and the stock from line to line (R-INV-09).
        List<(PostingLine Line, InventoryEntryRow Entry)> entries = [];
        List<JournalLineRow> journalLines = [];
        foreach (PostingLine line in lines)
        {
            BalanceKey key = new(line.ItemId, line.WarehouseId, line.StockStatus);
            Dictionary<string, string> details = new(StringComparer.Ordinal) { ["lineNumber"] = line.LineNumber.ToString(CultureInfo.InvariantCulture) };
            Costing costing;
            MovementType movement;
            if (line.QuantityBase > 0)
            {
                movement = MovementType.AdjustmentIn;
                Result<Costing> inbound = MovingAverageCost.Inbound(state[line.ItemId], line.QuantityBase, line.EnteredUnitCost, terms.MinorUnits, terms.RoundingMode);
                if (inbound.IsFailure)
                {
                    return Result<PostingOutcome>.Failure(inbound.Errors.Select(error => Error.Rule(error.RuleId!, error.Code, $"Line {line.LineNumber}: {error.Message}", details)));
                }

                costing = inbound.Value;
            }
            else
            {
                movement = MovementType.AdjustmentOut;
                decimal quantity = -line.QuantityBase;
                // R-INV-03 and R-INV-04: the balance key may not go below 0; Available stock is limited by its reservations (0 in milestone 1).
                BalanceRow balance = balances[key];
                decimal available = onHand[key] - (line.StockStatus == StockStatus.Available ? balance.Reserved : 0m);
                if (quantity > available)
                {
                    return Error.Rule("R-INV-03", StockInsufficient, $"Line {line.LineNumber}: {quantity} exceeds the {available} {line.StockStatus} on hand of item {line.ItemId} in warehouse {line.WarehouseId}.", details);
                }

                costing = MovingAverageCost.Outbound(state[line.ItemId], quantity, terms.MinorUnits, terms.RoundingMode);
            }

            state[line.ItemId] = costing.Next;
            onHand[key] += line.QuantityBase;
            decimal unitCost = costing.CostAmount == 0 ? 0m : MovingAverageCost.UnitCost(costing.CostAmount, line.QuantityBase);
            entries.Add((line, new InventoryEntryRow(EntityId.New(), line.ItemId, line.WarehouseId, line.StockStatus, movement, line.QuantityBase, costing.CostAmount, unitCost, line.LineId, JournalEntryId: null)));

            if (costing.CostAmount == 0)
            {
                // I-02, I-08: a zero-cost line writes its inventory entry and no journal lines.
                continue;
            }

            // E-01 (inbound: debit INVENTORY, credit INVENTORY_ADJUSTMENT) and E-02 (outbound: the reverse), the debit line first (D-15).
            Result<AccountResolution> inventory = await _accounts.ResolveAsync(unitOfWork, posting.CompanyId, AccountRoles.Inventory, posting.PostingDate, new PostingKeys(line.ItemPostingGroup), cancellationToken);
            if (inventory.IsFailure)
            {
                return Result<PostingOutcome>.Failure(inventory.Errors);
            }

            Result<AccountResolution> adjustment = await _accounts.ResolveAsync(unitOfWork, posting.CompanyId, AccountRoles.InventoryAdjustment, posting.PostingDate, new PostingKeys(line.ItemPostingGroup, ReasonCode: posting.ReasonCode), cancellationToken);
            if (adjustment.IsFailure)
            {
                return Result<PostingOutcome>.Failure(adjustment.Errors);
            }

            JournalLineRow inventoryLine = new(EntityId.New(), 0, inventory.Value.AccountId, inventory.Value.AccountRole, inventory.Value.PostingRuleId, costing.CostAmount, line.ItemId, line.WarehouseId, line.LineId);
            JournalLineRow adjustmentLine = new(EntityId.New(), 0, adjustment.Value.AccountId, adjustment.Value.AccountRole, adjustment.Value.PostingRuleId, -costing.CostAmount, line.ItemId, line.WarehouseId, line.LineId);
            journalLines.AddRange(costing.CostAmount > 0 ? [inventoryLine, adjustmentLine] : [adjustmentLine, inventoryLine]);
        }

        // Last lock class: the series the command uses, the journal entry series only when a journal entry is written (D-52); the IA number first, the JE number last (R-DOC-07).
        bool writesJournal = journalLines.Count > 0;
        List<NumberRequest> requests = [new(DocumentTypes.InventoryAdjustment)];
        if (writesJournal)
        {
            requests.Add(new NumberRequest(DocumentTypes.JournalEntry, posting.FiscalYearId));
        }

        Result<IReadOnlyList<NumberTaken>> numbers = await _numbers.TakeAsync(unitOfWork, posting.CompanyId, requests, now, by, cancellationToken);
        if (numbers.IsFailure)
        {
            return Result<PostingOutcome>.Failure(numbers.Errors);
        }

        string documentNumber = numbers.Value[0].Number;
        JournalEntryRow? entry = null;
        if (writesJournal)
        {
            string entryNumber = numbers.Value[1].Number;
            entry = new JournalEntryRow(EntityId.New(), entryNumber, posting.FiscalPeriodId, $"{JournalDescription} {documentNumber}", [.. journalLines.Select((journalLine, index) => journalLine with { LineNumber = index + 1 })]);
            // R-LED-02: the entry balances by construction; checked before anything is written.
            if (entry.Lines.Sum(journalLine => journalLine.Amount) != 0)
            {
                throw new InvalidOperationException($"Journal entry {entryNumber} does not balance (R-LED-02).");
            }
        }

        List<PostedLine> posted = [];
        try
        {
            await WriteAsync(unitOfWork, posting, entry, entries, costStates, state, balances, onHand, posted, commandId, now, by, cancellationToken);
        }
        catch (DbException exception) when (exception.SqlState == NumericValueOutOfRange)
        {
            // RR-83: only a 22003 from the posting's own statements becomes its rejection; anywhere else it stays a 500.
            throw new LedgerOverflow(exception);
        }

        await _hook.AfterLedgerWritesAsync(unitOfWork, posting.DocumentId, context, cancellationToken);
        return new PostingOutcome(documentNumber, entry?.Id, entry?.EntryNumber, posted);
    }

    private static async Task WriteAsync(
        IUnitOfWork unitOfWork,
        InventoryAdjustmentPosting posting,
        JournalEntryRow? entry,
        List<(PostingLine Line, InventoryEntryRow Entry)> entries,
        Dictionary<Guid, CostStateRow> costStates,
        Dictionary<Guid, CostState> state,
        Dictionary<BalanceKey, BalanceRow> balances,
        Dictionary<BalanceKey, decimal> onHand,
        List<PostedLine> posted,
        Guid commandId,
        DateTimeOffset now,
        UserId by,
        CancellationToken cancellationToken)
    {
        // The writes, in this order: the journal entry with its lines, the inventory entries, the cache updates (R-LED-05).
        if (entry is not null)
        {
            await LedgerStore.InsertJournalEntryAsync(unitOfWork, posting, entry, commandId, now, by, cancellationToken);
        }

        Dictionary<Guid, Guid> lastEntryByItem = [];
        Dictionary<BalanceKey, Guid> lastEntryByKey = [];
        foreach ((PostingLine line, InventoryEntryRow row) in entries)
        {
            // R-INV-06: an entry with a cost references the journal entry.
            InventoryEntryRow written = row with { JournalEntryId = row.CostAmount == 0 ? null : entry!.Id };
            await LedgerStore.InsertInventoryEntryAsync(unitOfWork, posting, written, commandId, now, by, cancellationToken);
            lastEntryByItem[line.ItemId] = written.Id;
            lastEntryByKey[new BalanceKey(line.ItemId, line.WarehouseId, line.StockStatus)] = written.Id;
            posted.Add(new PostedLine(line.LineId, written.Id, written.MovementType, written.CostAmount, written.UnitCost));
        }

        foreach ((Guid itemId, Guid lastEntry) in lastEntryByItem)
        {
            await LedgerStore.UpdateCostStateAsync(unitOfWork, costStates[itemId], state[itemId], lastEntry, now, by, cancellationToken);
        }

        foreach ((BalanceKey key, Guid lastEntry) in lastEntryByKey)
        {
            await LedgerStore.UpdateBalanceAsync(unitOfWork, balances[key], onHand[key], lastEntry, now, by, cancellationToken);
        }
    }

    /// <summary>A 22003 raised by the posting's own writes, carried to <see cref="PostInventoryAdjustmentAsync"/> (RR-83).</summary>
    private sealed class LedgerOverflow : Exception
    {
        public LedgerOverflow(DbException inner)
            : base("A posting write overflowed its column (SQLSTATE 22003).", inner)
        {
        }
    }
}

using Themis.Kernel.Contracts.Documents;

namespace Themis.Kernel.Documents;

/// <summary>
/// The number series fixture of milestone 1 step 7 (brief): for the step 3 company, INVENTORY_ADJUSTMENT continuous
/// with prefix IA- and padding 6, not gapless (IA-000001 first), and JOURNAL_ENTRY per fiscal year 2026 with prefix
/// JE2026- and padding 6, gapless (JE2026-000001 first). Company configuration, so fixture-seeded only, with an
/// audit event each and no outbox event. The identifiers are fixed so that seeding is idempotent and tests can
/// name the rows.
/// </summary>
internal static class Milestone1NumberSeriesFixture
{
    public static Guid InventoryAdjustmentSeriesId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000801");
    public static Guid JournalEntrySeriesId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000802");

    public const string InventoryAdjustmentPrefix = "IA-";
    public const string JournalEntryPrefix = "JE2026-";
    public const int Padding = 6;

    /// <summary>Builds the fixture for the company and fiscal year the organisation fixture seeds (step 3).</summary>
    public static NumberSeriesFixtureDefinition For(Guid companyId, Guid fiscalYearId) => new(
    [
        new NumberSeriesDefinition(InventoryAdjustmentSeriesId, companyId, DocumentTypes.InventoryAdjustment, NumberSeriesScope.Continuous, FiscalYearId: null, InventoryAdjustmentPrefix, Padding, NextNumber: 1, Gapless: false),
        new NumberSeriesDefinition(JournalEntrySeriesId, companyId, DocumentTypes.JournalEntry, NumberSeriesScope.PerFiscalYear, fiscalYearId, JournalEntryPrefix, Padding, NextNumber: 1, Gapless: true),
    ]);
}

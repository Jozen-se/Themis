using Themis.Kernel.Contracts.Settings;

namespace Themis.Kernel.Settings;

/// <summary>
/// The settings catalogue of this build (Phase 0 Specification, Settings entities; D-58), following the
/// PermissionCatalog pattern: every configurable default of the Foundation's classification list (P-11, D-29) is here
/// at least once, as a Setting, a Column-backed default, a Data-backed default or a Planned definition with its phase,
/// and nothing else is. Themis.Migrator writes the catalogue to kernel.setting_definition on every run and never removes
/// a row; a definition that leaves this list is reported and marked retired there. The architecture test
/// SettingCatalogCoverageTests holds the catalogue and the Foundation's list to each other in both directions.
/// </summary>
internal static class SettingCatalog
{
    private static readonly IReadOnlySet<SettingLevel> InstallationOnly = new HashSet<SettingLevel> { SettingLevel.Installation };
    private static readonly IReadOnlySet<SettingLevel> InstallationAndCompany = new HashSet<SettingLevel> { SettingLevel.Installation, SettingLevel.Company };
    private static readonly IReadOnlySet<SettingLevel> CompanyOnly = new HashSet<SettingLevel> { SettingLevel.Company };
    private static readonly IReadOnlySet<SettingLevel> CompanyAndWarehouse = new HashSet<SettingLevel> { SettingLevel.Company, SettingLevel.Warehouse };

    private const string Phase1 = "Phase 1 (purchasing and sales)";
    private const string Phase2 = "Phase 2 (finance)";

    public static IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        // Milestone 2 (Phase 0 Specification, Definitions in milestone 2).
        new(SettingKeys.IdempotencyRetentionDays, SettingValueType.Integer, "7", InstallationOnly, SettingBacking.Setting, "R-API-01",
            "Days an idempotency record lives after it was written. A change applies to records written after it; existing records keep their expires_at.",
            DecisionId: "D-45", Min: "1", Max: "90"),
        new(SettingKeys.DocumentMaxLines, SettingValueType.Integer, "1000", InstallationAndCompany, SettingBacking.Setting, "RR-54",
            "The most lines a document may hold; a draft with more lines is rejected before anything is stored.",
            Min: "1", Max: "10000"),
        new(SettingKeys.InventoryNegativeStockAllowed, SettingValueType.Boolean, "false", CompanyAndWarehouse, SettingBacking.Setting, "R-INV-03",
            "Whether stock on hand may go below zero. Off, and locked until a costing rule for negative quantities exists.",
            DecisionId: "D-09", LockedReason: "Allowing negative stock first needs a costing rule for negative quantities (D-09)."),
        new(SettingKeys.CompanyRoundingMode, SettingValueType.Code, "HalfAwayFromZero", CompanyOnly, SettingBacking.Column, "R-MON-02",
            "The company's rounding mode for amounts; Company.rounding_mode, changed by COMPANY.UPDATE.",
            AllowedValues: ["HalfAwayFromZero", "HalfEven"], ColumnName: "company.rounding_mode"),
        new(SettingKeys.CompanyTaxRoundingLevel, SettingValueType.Code, null, CompanyOnly, SettingBacking.Column, "R-MON-02",
            "Whether VAT rounds per line or per document total; Company.tax_rounding_level, changed by COMPANY.UPDATE. The default waits for D-10; Line is the test value.",
            DecisionId: "D-10", AllowedValues: ["Line", "Document"], ColumnName: "company.tax_rounding_level"),

        // Data-backed: the default is rows of an entity, changed by that entity's commands.
        new("security.sod_conflicts", SettingValueType.Code, "CUSTOMER.SET_CREDIT_LIMIT|SALES_ORDER.APPROVE_CREDIT,ACCOUNT.MAINTAIN|MANUAL_JOURNAL.POST,FISCAL_PERIOD.REOPEN|MANUAL_JOURNAL.POST", InstallationOnly, SettingBacking.Data, "R-SEC-02",
            "The permission pairs one user may not hold together. Rows of SodConflict, shipped by the migrator and changed by the role commands of milestone 2 step 13."),
        // R-SEC-03 entered the Foundation's list with D-62. The step 13 specification sets the value type and the D-62
        // defaults per document type; until then the definition holds no default.
        new("security.four_eyes_posting", SettingValueType.Boolean, null, CompanyOnly, SettingBacking.Planned, "R-SEC-03",
            "Four-eyes posting per document type: the user who posts a document is neither the user who created it nor the user who last changed it or one of its lines.",
            DecisionId: "D-62", Phase: "Phase 0, milestone 2 step 13 (users and roles)", PlannedLevels: "DocumentType"),

        // Planned: the feature that reads the default comes in the named phase (D-55 moves these out of Phase 0).
        new("inventory.availability_components", SettingValueType.Code, "OnHandMinusReservedMinusBlocked", CompanyOnly, SettingBacking.Planned, "R-INV-04",
            "Which stock statuses and future movements count as available; reservations arrive with Phase 1.", Phase: Phase1),
        new("inventory.costing_method", SettingValueType.Code, "MovingAverage", CompanyOnly, SettingBacking.Planned, "R-INV-05",
            "The costing method per company or item; FIFO later.", DecisionId: "D-08", AllowedValues: ["MovingAverage", "Fifo"], Phase: Phase2, PlannedLevels: "Item"),
        new("inventory.retroactive_cost_adjustment", SettingValueType.Boolean, "false", CompanyOnly, SettingBacking.Planned, "R-INV-09",
            "Retroactive cost adjustment (landed cost, price variances) through a batch cost-adjustment document, specified with Phase 2.", DecisionId: "D-33", Phase: Phase2),
        new("document.user_states", SettingValueType.Code, "SystemStatesOnly", CompanyOnly, SettingBacking.Planned, "R-DOC-01",
            "Extra user states, transitions and approvals per document type; the system states stay fixed.", Phase: Phase1, PlannedLevels: "DocumentType"),
        new("document.over_delivery_tolerance_percent", SettingValueType.Decimal, "0", CompanyOnly, SettingBacking.Planned, "R-DOC-03",
            "Over-delivery tolerance in percent, per customer; the limit itself is an invariant.", Min: "0", Max: "100", Phase: Phase1, PlannedLevels: "Customer"),
        new("document.discarded_draft_retention_days", SettingValueType.Integer, "0", CompanyOnly, SettingBacking.Planned, "R-DOC-06",
            "Days after which Discarded drafts that never received a number are purged; 0 keeps them. Cancelled documents are never purged.", Min: "0", Phase: Phase1),
        new("master_data.duplicate_check_fields", SettingValueType.Code, "OrganisationNumber,VatNumber,Name,PostalCode", CompanyOnly, SettingBacking.Planned, "R-MD-01",
            "The fields and threshold of the party duplicate check.", Phase: "Phase 0, milestone 2 step 8 (parties)"),
        new("master_data.owner_roles", SettingValueType.Code, "OneOwnerRolePerDomain", InstallationOnly, SettingBacking.Planned, "R-MD-04",
            "Which roles own which master data domain.", Phase: "Phase 0, milestone 2 step 13 (users and roles)"),
        new("tax.determination", SettingValueType.Code, "RuleBased", CompanyOnly, SettingBacking.Planned, "R-TAX-01",
            "Tax rules per company; a manual override needs a permission and is stored as an override record.", Phase: Phase2),
        new("fiscal_period.reopen_closed_allowed", SettingValueType.Boolean, "false", CompanyOnly, SettingBacking.Planned, "R-PER-01",
            "Whether a Closed period may be reopened with permission, where law allows; final in v1.", Phase: Phase2),
        new("sales.credit_check_mode", SettingValueType.Code, "Block", CompanyOnly, SettingBacking.Planned, "R-O2C-01",
            "Block, warn or off, per customer group.", AllowedValues: ["Block", "Warn", "Off"], Phase: Phase1, PlannedLevels: "CustomerGroup"),
        new("sales.allocation_priority", SettingValueType.Code, "RequestedDateThenReleaseTime", CompanyOnly, SettingBacking.Planned, "R-O2C-02",
            "The order in which released orders are allocated stock.", Phase: Phase1),
        new("sales.partial_delivery_allowed", SettingValueType.Boolean, "true", CompanyOnly, SettingBacking.Planned, "R-O2C-03",
            "Partial delivery allowed, or complete delivery only, per customer or order.", Phase: Phase1, PlannedLevels: "Customer, SalesOrder"),
        new("sales.goods_issue_posting_point", SettingValueType.Code, "Shipment", CompanyOnly, SettingBacking.Planned, "R-O2C-04",
            "When the goods issue posts.", Phase: Phase1),
        new("sales.invoice_grouping", SettingValueType.Code, "PerCustomer", CompanyOnly, SettingBacking.Planned, "R-O2C-05",
            "Invoice grouping: per delivery, collective per customer or periodic; the billing limit itself is an invariant.", AllowedValues: ["PerDelivery", "PerCustomer", "Periodic"], Phase: Phase2),
        new("sales.price_sources", SettingValueType.Code, "Agreement,CustomerList,BaseList", CompanyOnly, SettingBacking.Planned, "R-O2C-06",
            "Price sources and their search order.", Phase: Phase1),
        new("payments.difference_tolerance", SettingValueType.Decimal, "0", CompanyOnly, SettingBacking.Planned, "R-O2C-07",
            "The payment difference written off without a dispute, in company currency, with its write-off account.", Min: "0", Phase: Phase2),
        new("sales.return_valuation", SettingValueType.Code, "OriginalIssueCost", CompanyOnly, SettingBacking.Planned, "R-O2C-08",
            "Returns valued at the original issue cost or at the current average cost.", DecisionId: "D-11", AllowedValues: ["OriginalIssueCost", "CurrentAverageCost"], Phase: Phase1),
    ];

    public static SettingDefinition? Find(string key) => Definitions.FirstOrDefault(definition => string.Equals(definition.Key, key, StringComparison.Ordinal));
}

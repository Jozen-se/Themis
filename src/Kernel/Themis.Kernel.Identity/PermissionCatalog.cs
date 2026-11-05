using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Identity;

/// <summary>
/// The command catalogue of this build as permissions (Phase 0 Specification, Security entities): one permission
/// per command and one TYPE.READ per resource type with a query (D-47). Themis.Migrator seeds the catalogue on
/// every run and never removes a permission. A unit test holds the catalogue and the command and query types to
/// each other in both directions: every type names a permission here, and every permission here has its type, or
/// is listed in <see cref="AwaitingCommands"/> with the milestone step that builds the command (Build Brief, step 4
/// named the INVENTORY_ADJUSTMENT verbs ahead of steps 7 and 8, which built them).
/// </summary>
internal static class PermissionCatalog
{
    public const string CompanyRead = "COMPANY.READ";
    public const string ItemCreate = "ITEM.CREATE";
    public const string ItemRead = "ITEM.READ";
    public const string InventoryAdjustmentCreate = "INVENTORY_ADJUSTMENT.CREATE";
    public const string InventoryAdjustmentUpdate = "INVENTORY_ADJUSTMENT.UPDATE";
    public const string InventoryAdjustmentPost = "INVENTORY_ADJUSTMENT.POST";
    public const string InventoryAdjustmentDiscard = "INVENTORY_ADJUSTMENT.DISCARD";
    public const string InventoryAdjustmentRead = "INVENTORY_ADJUSTMENT.READ";
    public const string SettingSet = "SETTING.SET";
    public const string SettingClear = "SETTING.CLEAR";
    public const string SettingRead = "SETTING.READ";
    public const string CompanyCreate = "COMPANY.CREATE";
    public const string CompanyUpdate = "COMPANY.UPDATE";
    public const string ChartOfAccountsCreate = "CHART_OF_ACCOUNTS.CREATE";
    public const string ChartOfAccountsUpdate = "CHART_OF_ACCOUNTS.UPDATE";
    public const string ChartOfAccountsRead = "CHART_OF_ACCOUNTS.READ";
    public const string SiteCreate = "SITE.CREATE";
    public const string SiteUpdate = "SITE.UPDATE";
    public const string SiteRead = "SITE.READ";
    public const string WarehouseCreate = "WAREHOUSE.CREATE";
    public const string WarehouseUpdate = "WAREHOUSE.UPDATE";
    public const string WarehouseRead = "WAREHOUSE.READ";
    public const string AccountCreate = "ACCOUNT.CREATE";
    public const string AccountUpdate = "ACCOUNT.UPDATE";
    public const string AccountBlock = "ACCOUNT.BLOCK";
    public const string AccountUnblock = "ACCOUNT.UNBLOCK";
    public const string AccountDeactivate = "ACCOUNT.DEACTIVATE";
    public const string AccountReactivate = "ACCOUNT.REACTIVATE";
    public const string AccountRead = "ACCOUNT.READ";

    // The specification's v1 conflict pairs name these commands of later steps and phases; they are pairs, not permissions, until their commands exist.
    public const string CustomerSetCreditLimit = "CUSTOMER.SET_CREDIT_LIMIT";
    public const string SalesOrderApproveCredit = "SALES_ORDER.APPROVE_CREDIT";
    public const string ManualJournalPost = "MANUAL_JOURNAL.POST";
    public const string FiscalPeriodReopen = "FISCAL_PERIOD.REOPEN";

    public static PermissionCatalogDefinition Definition { get; } = new(
        [
            new(CompanyRead, PermissionRiskLevel.Normal),
            new(ItemCreate, PermissionRiskLevel.Normal),
            new(ItemRead, PermissionRiskLevel.Normal),
            new(InventoryAdjustmentCreate, PermissionRiskLevel.Normal),
            new(InventoryAdjustmentUpdate, PermissionRiskLevel.Normal),
            // Step 4 brief: INVENTORY_ADJUSTMENT.POST is Sensitive, the rest Normal.
            new(InventoryAdjustmentPost, PermissionRiskLevel.Sensitive),
            new(InventoryAdjustmentDiscard, PermissionRiskLevel.Normal),
            new(InventoryAdjustmentRead, PermissionRiskLevel.Normal),
            // Step 2 brief (milestone 2): SETTING.SET and SETTING.CLEAR are Sensitive; an installation value is changed by a holder
            // with a company-wide assignment in any company (RR-52), so access reviews treat them as installation-wide.
            new(SettingSet, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(SettingClear, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(SettingRead, PermissionRiskLevel.Normal, InstallationScope: true),
            // Step 3 brief (milestone 2): COMPANY.CREATE, COMPANY.UPDATE and the chart header commands are Sensitive; COMPANY.CREATE
            // and the chart commands act installation-wide (RR-52), as access reviews treat SETTING.SET. The site and warehouse
            // commands and the four read permissions are Normal.
            new(CompanyCreate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(CompanyUpdate, PermissionRiskLevel.Sensitive),
            new(ChartOfAccountsCreate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(ChartOfAccountsUpdate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(ChartOfAccountsRead, PermissionRiskLevel.Normal, InstallationScope: true),
            new(SiteCreate, PermissionRiskLevel.Normal),
            new(SiteUpdate, PermissionRiskLevel.Normal),
            new(SiteRead, PermissionRiskLevel.Normal),
            new(WarehouseCreate, PermissionRiskLevel.Normal),
            new(WarehouseUpdate, PermissionRiskLevel.Normal),
            new(WarehouseRead, PermissionRiskLevel.Normal),
            // Step 4 brief (milestone 2; D-69): the six account commands are installation-scope and Sensitive, ACCOUNT.READ is Normal.
            new(AccountCreate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(AccountUpdate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(AccountBlock, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(AccountUnblock, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(AccountDeactivate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(AccountReactivate, PermissionRiskLevel.Sensitive, InstallationScope: true),
            new(AccountRead, PermissionRiskLevel.Normal, InstallationScope: true),
        ],
        [
            // The specification's example conflict pairs for v1 (R-SEC-02), each stored with its codes in order.
            Pair(CustomerSetCreditLimit, SalesOrderApproveCredit),
            // D-69: ACCOUNT.MAINTAIN with MANUAL_JOURNAL.POST is replaced by these four; a migration deletes its row (it was never a permission).
            Pair(AccountCreate, ManualJournalPost),
            Pair(AccountUpdate, ManualJournalPost),
            Pair(AccountUnblock, ManualJournalPost),
            Pair(AccountReactivate, ManualJournalPost),
            Pair(FiscalPeriodReopen, ManualJournalPost),
        ]);

    /// <summary>Permissions seeded ahead of their commands, with the milestone step that builds the command. Empty since step 8 built INVENTORY_ADJUSTMENT.POST; later steps add their permissions here first.</summary>
    public static IReadOnlyDictionary<string, string> AwaitingCommands { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>A pair with its codes in ordinal order, so (a, b) and (b, a) are the same row.</summary>
    public static SodConflictDefinition Pair(string first, string second)
    {
        PermissionCodes.Validated(first);
        PermissionCodes.Validated(second);
        if (string.Equals(first, second, StringComparison.Ordinal))
        {
            throw new ArgumentException("A conflict pair names two different permissions.", nameof(second));
        }

        return string.CompareOrdinal(first, second) < 0 ? new SodConflictDefinition(first, second) : new SodConflictDefinition(second, first);
    }

    public static bool Contains(string permissionCode) =>
        Definition.Permissions.Any(permission => string.Equals(permission.Code, permissionCode, StringComparison.Ordinal));
}

using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Identity;

/// <summary>
/// The command catalogue of this build as permissions (Phase 0 Specification, Security entities): one permission
/// per command and one TYPE.READ per resource type with a query (D-47). Themis.Migrator seeds the catalogue on
/// every run and never removes a permission. A unit test holds the catalogue and the command and query types to
/// each other in both directions: every type names a permission here, and every permission here has its type, or
/// is listed in <see cref="AwaitingCommands"/> with the milestone step that builds the command (Build Brief, step 4
/// names the INVENTORY_ADJUSTMENT verbs ahead of step 7).
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

    // The specification's v1 conflict pairs name these commands of later steps and phases; they are pairs, not permissions, until their commands exist.
    public const string CustomerSetCreditLimit = "CUSTOMER.SET_CREDIT_LIMIT";
    public const string SalesOrderApproveCredit = "SALES_ORDER.APPROVE_CREDIT";
    public const string AccountMaintain = "ACCOUNT.MAINTAIN";
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
        ],
        [
            // The specification's example conflict pairs for v1 (R-SEC-02), each stored with its codes in order.
            Pair(CustomerSetCreditLimit, SalesOrderApproveCredit),
            Pair(AccountMaintain, ManualJournalPost),
            Pair(FiscalPeriodReopen, ManualJournalPost),
        ]);

    /// <summary>Permissions seeded ahead of their commands, with the milestone step that builds the command. Shrinks as the steps land.</summary>
    public static IReadOnlyDictionary<string, string> AwaitingCommands { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [InventoryAdjustmentCreate] = "milestone 1 step 7",
        [InventoryAdjustmentUpdate] = "milestone 1 step 7",
        [InventoryAdjustmentPost] = "milestone 1 step 7",
        [InventoryAdjustmentDiscard] = "milestone 1 step 7",
        [InventoryAdjustmentRead] = "milestone 1 step 7",
    };

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

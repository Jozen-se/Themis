using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Identity;

/// <summary>
/// The identity fixture of milestone 1 step 4 (approved step brief): two test users mapped to the subjects of the
/// Keycloak realm in docker/keycloak/themis-realm.json. The clerk holds roles giving ITEM.CREATE and
/// INVENTORY_ADJUSTMENT.CREATE, UPDATE, POST and DISCARD; the preparer the same without POST; both are assigned to
/// company SE01 without site restriction. Both also hold the TYPE.READ permissions of the milestone's resource
/// types (D-47). Every value is test data; the identifiers are fixed so that seeding is idempotent and tests can
/// name the rows.
/// </summary>
internal static class Milestone1IdentityFixture
{
    public static Guid ClerkId { get; } = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static Guid PreparerId { get; } = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static Guid PreparerRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000301");
    public static Guid PosterRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000302");
    public static Guid ClerkPreparerAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000401");
    public static Guid ClerkPosterAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000402");
    public static Guid PreparerAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000403");

    /// <summary>The Keycloak user ids of the realm export, which the provider issues as the <c>sub</c> claim.</summary>
    public const string ClerkSubject = "cccccccc-1111-4111-8111-cccccccccccc";
    public const string PreparerSubject = "dddddddd-2222-4222-8222-dddddddddddd";

    public const string ClerkUsername = "clerk";
    public const string PreparerUsername = "preparer";

    /// <summary>Development passwords of the realm export; never used outside the local stack and the tests.</summary>
    public const string ClerkPassword = "clerk";
    public const string PreparerPassword = "preparer";

    /// <summary>Prepares adjustments: everything but posting.</summary>
    public const string PreparerRoleCode = "INVENTORY_PREPARER";

    /// <summary>Posts adjustments: the Sensitive permission alone, so the two roles never collide (R-SEC-02).</summary>
    public const string PosterRoleCode = "INVENTORY_POSTER";

    /// <summary>Assignments start with the fixture's fiscal year and never end.</summary>
    public static BusinessDate ValidFrom { get; } = BusinessDate.Parse("2026-01-01");

    /// <summary>Builds the fixture for the company the organisation fixture seeds (step 3).</summary>
    public static IdentityFixtureDefinition For(Guid companyId) => new(
        [
            new UserDefinition(ClerkId, ClerkSubject, "Clerk Carlsson", "clerk@themis.local", IsServiceAccount: false, UserStatus.Active),
            new UserDefinition(PreparerId, PreparerSubject, "Preparer Persson", "preparer@themis.local", IsServiceAccount: false, UserStatus.Active),
        ],
        [
            new RoleDefinition(PreparerRoleId, Code.From(PreparerRoleCode), "Inventory preparer",
            [
                PermissionCatalog.CompanyRead,
                PermissionCatalog.ItemCreate,
                PermissionCatalog.ItemRead,
                PermissionCatalog.InventoryAdjustmentCreate,
                PermissionCatalog.InventoryAdjustmentUpdate,
                PermissionCatalog.InventoryAdjustmentDiscard,
                PermissionCatalog.InventoryAdjustmentRead,
            ]),
            new RoleDefinition(PosterRoleId, Code.From(PosterRoleCode), "Inventory poster",
            [
                PermissionCatalog.InventoryAdjustmentPost,
            ]),
        ],
        [
            new RoleAssignmentDefinition(ClerkPreparerAssignmentId, ClerkId, PreparerRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(ClerkPosterAssignmentId, ClerkId, PosterRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(PreparerAssignmentId, PreparerId, PreparerRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
        ]);
}

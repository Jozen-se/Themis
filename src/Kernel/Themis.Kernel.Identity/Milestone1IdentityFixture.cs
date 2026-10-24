using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Identity;

/// <summary>
/// The identity fixture of milestone 1 step 4 (approved step brief): two test users mapped to the subjects of the
/// Keycloak realm in docker/keycloak/themis-realm.json. The clerk holds roles giving ITEM.CREATE and
/// INVENTORY_ADJUSTMENT.CREATE, UPDATE, POST and DISCARD; the preparer prepares adjustments only, without POST and
/// without ITEM.CREATE (step 5 brief, acceptance 6: the preparer without ITEM.CREATE gets 403); both are assigned
/// to company SE01 without site restriction. Both also hold the TYPE.READ permissions of the milestone's resource
/// types (D-47). Since milestone 2 step 2a a third user, the settings administrator, holds SETTINGS_ADMINISTRATOR,
/// so the clerk's permissions are again exactly those milestone 1 proves (RR-101), and since milestone 2 step 3 a fourth,
/// the organisation administrator, holds ORGANISATION_ADMINISTRATOR; neither is in the Keycloak realm, because no
/// Keycloak test needs them. Every value is test data; the identifiers are fixed so that seeding is idempotent and
/// tests can name the rows.
/// </summary>
internal static class Milestone1IdentityFixture
{
    public static Guid ClerkId { get; } = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static Guid PreparerId { get; } = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static Guid SettingsAdministratorId { get; } = Guid.Parse("33333333-3333-4333-8333-333333333333");
    public static Guid OrganisationAdministratorId { get; } = Guid.Parse("44444444-4444-4444-8444-444444444444");
    public static Guid PreparerRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000301");
    public static Guid PosterRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000302");
    public static Guid ItemMaintainerRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000303");
    public static Guid SettingsAdministratorRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000304");
    public static Guid OrganisationAdministratorRoleId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000305");
    public static Guid ClerkPreparerAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000401");
    public static Guid ClerkPosterAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000402");
    public static Guid PreparerAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000403");
    public static Guid ClerkItemMaintainerAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000404");
    public static Guid SettingsAdministratorAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000406");
    public static Guid OrganisationAdministratorAssignmentId { get; } = Guid.Parse("00000000-0000-7000-8000-000000000407");

    /// <summary>The Keycloak user ids of the realm export, which the provider issues as the <c>sub</c> claim.</summary>
    public const string ClerkSubject = "cccccccc-1111-4111-8111-cccccccccccc";
    public const string PreparerSubject = "dddddddd-2222-4222-8222-dddddddddddd";

    /// <summary>The settings administrator's subject; test tokens only, not in the Keycloak realm (RR-101).</summary>
    public const string SettingsAdministratorSubject = "eeeeeeee-3333-4333-8333-eeeeeeeeeeee";

    /// <summary>The organisation administrator's subject (milestone 2 step 3); test tokens only, not in the Keycloak realm.</summary>
    public const string OrganisationAdministratorSubject = "ffffffff-4444-4444-8444-ffffffffffff";

    public const string ClerkUsername = "clerk";
    public const string PreparerUsername = "preparer";

    /// <summary>Development passwords of the realm export; never used outside the local stack and the tests.</summary>
    public const string ClerkPassword = "clerk";
    public const string PreparerPassword = "preparer";

    /// <summary>Prepares adjustments: everything but posting, and no item maintenance.</summary>
    public const string PreparerRoleCode = "INVENTORY_PREPARER";

    /// <summary>Maintains items: ITEM.CREATE with its read, held by the clerk only (step 5 brief, acceptance 6).</summary>
    public const string ItemMaintainerRoleCode = "ITEM_MAINTAINER";

    /// <summary>Posts adjustments: the Sensitive permission alone, so the two roles never collide (R-SEC-02).</summary>
    public const string PosterRoleCode = "INVENTORY_POSTER";

    /// <summary>Administers settings (milestone 2 step 2): SETTING.SET, SETTING.CLEAR and SETTING.READ, held by the settings administrator only (RR-101).</summary>
    public const string SettingsAdministratorRoleCode = "SETTINGS_ADMINISTRATOR";

    /// <summary>
    /// Administers the organisation (milestone 2 step 3): the chart of accounts header, company, site and warehouse
    /// commands with their reads, held by the organisation administrator only, so the clerk's permissions stay those
    /// milestone 1 proves. Its company-wide assignment in SE01 satisfies the installation-scope commands (RR-52).
    /// </summary>
    public const string OrganisationAdministratorRoleCode = "ORGANISATION_ADMINISTRATOR";

    /// <summary>Assignments start with the fixture's fiscal year and never end.</summary>
    public static BusinessDate ValidFrom { get; } = BusinessDate.Parse("2026-01-01");

    /// <summary>Builds the fixture for the company the organisation fixture seeds (step 3).</summary>
    public static IdentityFixtureDefinition For(Guid companyId) => new(
        [
            new UserDefinition(ClerkId, ClerkSubject, "Clerk Carlsson", "clerk@themis.local", IsServiceAccount: false, UserStatus.Active),
            new UserDefinition(PreparerId, PreparerSubject, "Preparer Persson", "preparer@themis.local", IsServiceAccount: false, UserStatus.Active),
            new UserDefinition(SettingsAdministratorId, SettingsAdministratorSubject, "Settings Svensson", "settings@themis.local", IsServiceAccount: false, UserStatus.Active),
            new UserDefinition(OrganisationAdministratorId, OrganisationAdministratorSubject, "Organisation Olsson", "organisation@themis.local", IsServiceAccount: false, UserStatus.Active),
        ],
        [
            new RoleDefinition(PreparerRoleId, Code.From(PreparerRoleCode), "Inventory preparer",
            [
                PermissionCatalog.CompanyRead,
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
            new RoleDefinition(ItemMaintainerRoleId, Code.From(ItemMaintainerRoleCode), "Item maintainer",
            [
                PermissionCatalog.ItemCreate,
                PermissionCatalog.ItemRead,
            ]),
            new RoleDefinition(SettingsAdministratorRoleId, Code.From(SettingsAdministratorRoleCode), "Settings administrator",
            [
                PermissionCatalog.SettingSet,
                PermissionCatalog.SettingClear,
                PermissionCatalog.SettingRead,
            ]),
            new RoleDefinition(OrganisationAdministratorRoleId, Code.From(OrganisationAdministratorRoleCode), "Organisation administrator",
            [
                PermissionCatalog.CompanyCreate,
                PermissionCatalog.CompanyUpdate,
                PermissionCatalog.CompanyRead,
                PermissionCatalog.ChartOfAccountsCreate,
                PermissionCatalog.ChartOfAccountsUpdate,
                PermissionCatalog.ChartOfAccountsRead,
                PermissionCatalog.SiteCreate,
                PermissionCatalog.SiteUpdate,
                PermissionCatalog.SiteRead,
                PermissionCatalog.WarehouseCreate,
                PermissionCatalog.WarehouseUpdate,
                PermissionCatalog.WarehouseRead,
            ]),
        ],
        [
            new RoleAssignmentDefinition(ClerkPreparerAssignmentId, ClerkId, PreparerRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(ClerkPosterAssignmentId, ClerkId, PosterRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(PreparerAssignmentId, PreparerId, PreparerRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(ClerkItemMaintainerAssignmentId, ClerkId, ItemMaintainerRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(SettingsAdministratorAssignmentId, SettingsAdministratorId, SettingsAdministratorRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
            new RoleAssignmentDefinition(OrganisationAdministratorAssignmentId, OrganisationAdministratorId, OrganisationAdministratorRoleId, companyId, SiteId: null, ValidFrom, ValidTo: null),
        ]);
}

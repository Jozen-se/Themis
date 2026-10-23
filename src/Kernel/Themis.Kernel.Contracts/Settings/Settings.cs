using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Settings;

/// <summary>
/// SETTING.SET (step 2 brief; D-58, R-SET-01): writes one value row for a definition at a level and target, version
/// current + 1 under If-Match (R-API-02; 0 for a slot with no rows yet). The target names the company (and site or
/// warehouse) the level needs. The scope comes from the settings scope resolver inside the transaction: an Installation
/// value declares installation scope (RR-52), a Warehouse value the warehouse's company and site (D-63, RR-102). A
/// history-bearing definition takes the caller's valid_from; a plain one refuses it.
/// </summary>
[Command("SETTING.SET")]
public sealed record SetSetting(
    string Key,
    SettingLevel Level,
    string Value,
    long ExpectedVersion,
    Guid? CompanyId = null,
    Guid? SiteId = null,
    Guid? WarehouseId = null,
    BusinessDate? ValidFrom = null) : ICommand<SettingValueView>, IVersionedCommand;

/// <summary>SETTING.CLEAR (step 2 brief; D-58): inserts a Cleared row at the slot, so the level falls through; nothing is updated or deleted. Scoped like SETTING.SET (D-63, RR-102).</summary>
[Command("SETTING.CLEAR")]
public sealed record ClearSetting(
    string Key,
    SettingLevel Level,
    long ExpectedVersion,
    Guid? CompanyId = null,
    Guid? SiteId = null,
    Guid? WarehouseId = null,
    BusinessDate? ValidFrom = null) : ICommand<SettingValueView>, IVersionedCommand;

/// <summary>Phase 0 Specification, SettingValue.kind.</summary>
public enum SettingValueKind
{
    Set,
    Cleared,
}

/// <summary>The row a write inserted (Phase 0 Specification, SettingValue); the version is the slot's new version (R-API-02).</summary>
public sealed record SettingValueView(
    Guid Id,
    string Key,
    SettingLevel Level,
    Guid? CompanyId,
    Guid? SiteId,
    Guid? WarehouseId,
    SettingValueKind Kind,
    string? Value,
    BusinessDate? ValidFrom,
    long Version,
    DateTimeOffset RecordedAt) : IVersionedResult;

/// <summary>
/// GET /v1/kernel/settings/{key} with the target in the query string (SETTING.READ, D-47): the effective value at the
/// target with its source, and the current version of each level the definition allows at that target. A warehouse
/// target declares the warehouse's company and site, resolved like the writes (D-63, RR-102).
/// </summary>
[Query("SETTING.READ")]
public sealed record GetSetting(string Key, Guid? CompanyId = null, Guid? SiteId = null, Guid? WarehouseId = null) : IQuery<SettingReadView>;

/// <summary>The state of one level at the target: its current version (0 when no row), and the deciding row when there is one.</summary>
public sealed record SettingLevelState(SettingLevel Level, long Version, SettingValueKind? Kind, string? Value, BusinessDate? ValidFrom);

/// <summary>
/// SETTING.READ's answer: the definition's shape, the effective value and its source, and the levels at the target.
/// A Data or Planned definition has no value type (D-63). A Data definition answers with source Data and its entity's
/// rows, read in the query's transaction: for security.sod_conflicts, the conflict pairs (R-SEC-02).
/// </summary>
public sealed record SettingReadView(
    string Key,
    SettingValueType? ValueType,
    SettingBacking Backing,
    string? DefaultValue,
    string? LockedReason,
    IReadOnlyList<SettingLevel> AllowedLevels,
    string? Value,
    SettingSource Source,
    IReadOnlyList<SettingLevelState> Levels,
    IReadOnlyList<PermissionCodePair>? ConflictPairs = null);

/// <summary>One SodConflict row as SETTING.READ returns it: two permission codes one user may not hold together (R-SEC-02).</summary>
public sealed record PermissionCodePair(string PermissionA, string PermissionB);

public static class SettingRoutes
{
    public const string Set = "/v1/kernel/settings/set";
    public const string Clear = "/v1/kernel/settings/clear";
    public const string Setting = "/v1/kernel/settings/{key}";

    /// <summary>The command routes the composition root maps through the kernel pipeline (brief, API conventions).</summary>
    public static IReadOnlyList<ICommandRoute> Commands { get; } =
    [
        new CommandRoute<SetSetting, SettingValueView>(Set),
        new CommandRoute<ClearSetting, SettingValueView>(Clear),
    ];
}

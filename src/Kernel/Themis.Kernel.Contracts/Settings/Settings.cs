using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Settings;

/// <summary>
/// SETTING.SET (step 2 brief; D-58, R-SET-01): writes one value row for a definition at a level and target, version
/// current + 1 under If-Match (R-API-02; 0 for a slot with no rows yet). The target names the company (and site) the
/// level needs; an Installation value declares installation scope (RR-52). A history-bearing definition takes the
/// caller's valid_from; a plain one refuses it.
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
    BusinessDate? ValidFrom = null) : ICommand<SettingValueView>, IVersionedCommand, IScopedRequest
{
    public RequestScope Scope => SettingScopes.Of(Level, CompanyId, SiteId);
}

/// <summary>SETTING.CLEAR (step 2 brief; D-58): inserts a Cleared row at the slot, so the level falls through; nothing is updated or deleted.</summary>
[Command("SETTING.CLEAR")]
public sealed record ClearSetting(
    string Key,
    SettingLevel Level,
    long ExpectedVersion,
    Guid? CompanyId = null,
    Guid? SiteId = null,
    Guid? WarehouseId = null,
    BusinessDate? ValidFrom = null) : ICommand<SettingValueView>, IVersionedCommand, IScopedRequest
{
    public RequestScope Scope => SettingScopes.Of(Level, CompanyId, SiteId);
}

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
/// target with its source, and the current version of each level the definition allows at that target.
/// </summary>
[Query("SETTING.READ")]
public sealed record GetSetting(string Key, Guid? CompanyId = null, Guid? SiteId = null, Guid? WarehouseId = null) : IQuery<SettingReadView>, IScopedRequest
{
    public RequestScope Scope => CompanyId is { } company
        ? SiteId is { } site ? RequestScope.ForSite(company, site) : RequestScope.ForCompany(company)
        : RequestScope.Installation;
}

/// <summary>The state of one level at the target: its current version (0 when no row), and the deciding row when there is one.</summary>
public sealed record SettingLevelState(SettingLevel Level, long Version, SettingValueKind? Kind, string? Value, BusinessDate? ValidFrom);

/// <summary>SETTING.READ's answer: the definition's shape, the effective value and its source, and the levels at the target.</summary>
public sealed record SettingReadView(
    string Key,
    SettingValueType ValueType,
    SettingBacking Backing,
    string? DefaultValue,
    string? LockedReason,
    IReadOnlyList<SettingLevel> AllowedLevels,
    string? Value,
    SettingSource Source,
    IReadOnlyList<SettingLevelState> Levels);

/// <summary>The scope a write declares (Build Brief, Authorisation; RR-52): the company (and site) its level names, or the installation.</summary>
public static class SettingScopes
{
    public static RequestScope Of(SettingLevel level, Guid? companyId, Guid? siteId) => level switch
    {
        SettingLevel.Installation => RequestScope.Installation,
        SettingLevel.Site when companyId is { } company && siteId is { } site => RequestScope.ForSite(company, site),
        _ when companyId is { } company => RequestScope.ForCompany(company),
        // The validators reject a level without its target before the scope is read (RR-51); this keeps the property total.
        _ => RequestScope.Installation,
    };
}

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

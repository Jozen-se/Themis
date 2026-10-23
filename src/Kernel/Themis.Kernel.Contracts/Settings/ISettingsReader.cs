using Themis.Kernel.Contracts.Persistence;

namespace Themis.Kernel.Contracts.Settings;

/// <summary>
/// Where a value is asked for: the warehouse, its site and its company narrow the search; an empty target reads
/// installation values and the default only. A warehouse given without its site is completed from the warehouse row.
/// </summary>
public readonly record struct SettingTarget(Guid? CompanyId = null, Guid? SiteId = null, Guid? WarehouseId = null)
{
    public static SettingTarget Installation => default;

    public static SettingTarget ForCompany(Guid companyId) => new(companyId);

    public static SettingTarget ForSite(Guid companyId, Guid siteId) => new(companyId, siteId);

    public static SettingTarget ForWarehouse(Guid companyId, Guid warehouseId, Guid? siteId = null) => new(companyId, siteId, warehouseId);
}

/// <summary>Where an effective value came from (Phase 0 Specification, SETTING.READ): a level, the definition's default, an entity's column, or the rows of a Data definition's entity (D-63).</summary>
public enum SettingSource
{
    Default,
    Column,
    Installation,
    Company,
    Site,
    Warehouse,
    Data,
}

/// <summary>The effective value of a setting at a target and where it came from; the value is the definition's text form.</summary>
public sealed record SettingResolution(string Key, string? Value, SettingSource Source);

/// <summary>
/// Reads settings inside a command's own transaction (D-58): the first value found at the warehouse, then its site,
/// then its company, then installation, then the definition's default; a Cleared row means no value at that level and a
/// level the definition does not allow is skipped. No cache across requests in v1 (D-41).
/// </summary>
public interface ISettingsReader
{
    Task<SettingResolution> ResolveAsync(IUnitOfWork unitOfWork, string key, SettingTarget target, CancellationToken cancellationToken);

    /// <summary>The effective value of an Integer definition, parsed; the definition's default when nothing is set.</summary>
    Task<int> GetIntegerAsync(IUnitOfWork unitOfWork, string key, SettingTarget target, CancellationToken cancellationToken);
}

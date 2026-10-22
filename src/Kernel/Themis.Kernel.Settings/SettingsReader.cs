using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Settings;

/// <summary>What one level holds at a target: the deciding row, or none; used by the reader and by SETTING.READ.</summary>
internal sealed record LevelDecision(SettingLevel Level, long Version, SettingValueRecord? Deciding);

/// <summary>
/// The settings reader (Phase 0 Specification, Resolution; D-58): for one definition, level and target, a plain
/// definition takes the row with the highest version; a history-bearing one the row with the latest valid_from on or
/// before the business date, ties going to the highest version (R-TIME-01, in the company's time zone; installation
/// values use the requesting company's date, or the UTC date when no company is named). A Cleared row means no value
/// at that level. The effective value is the first found at the warehouse, then its site, then its company, then
/// installation, then the definition's default; a level the definition does not allow is skipped. A Column-backed
/// definition reads its entity's column. Everything runs inside the caller's transaction; no cache across requests
/// in v1 (D-41).
/// </summary>
internal sealed class SettingsReader : ISettingsReader
{
    /// <summary>Most specific first, the order the levels are searched in.</summary>
    public static IReadOnlyList<SettingLevel> SearchOrder { get; } = [SettingLevel.Warehouse, SettingLevel.Site, SettingLevel.Company, SettingLevel.Installation];

    private readonly SettingDefinitions _definitions;
    private readonly IOrganisationReader _organisation;
    private readonly TimeProvider _time;

    public SettingsReader(SettingDefinitions definitions, IOrganisationReader organisation, TimeProvider time)
    {
        _definitions = definitions;
        _organisation = organisation;
        _time = time;
    }

    public async Task<SettingResolution> ResolveAsync(IUnitOfWork unitOfWork, string key, SettingTarget target, CancellationToken cancellationToken)
    {
        SettingDefinition definition = _definitions.Find(key) ?? throw new InvalidOperationException($"Setting {key} is not in the catalogue.");
        Resolved resolved = await ResolveAsync(unitOfWork, definition, target, asOf: null, cancellationToken);
        return new SettingResolution(key, resolved.Value, resolved.Source);
    }

    public async Task<int> GetIntegerAsync(IUnitOfWork unitOfWork, string key, SettingTarget target, CancellationToken cancellationToken)
    {
        SettingDefinition definition = _definitions.Find(key) ?? throw new InvalidOperationException($"Setting {key} is not in the catalogue.");
        if (definition.ValueType != SettingValueType.Integer)
        {
            throw new InvalidOperationException($"Setting {key} is {definition.ValueType}, not Integer.");
        }

        Resolved resolved = await ResolveAsync(unitOfWork, definition, target, asOf: null, cancellationToken);
        return resolved.Value is { } text
            ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"Setting {key} has no value and no default.");
    }

    /// <summary>The effective value with its source and the state of every allowed level at the target (SETTING.READ).</summary>
    public async Task<Resolved> ResolveAsync(IUnitOfWork unitOfWork, SettingDefinition definition, SettingTarget target, BusinessDate? asOf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        target = await CompleteAsync(unitOfWork, target, cancellationToken);
        IReadOnlyList<SettingValueRecord> rows = definition.Backing == SettingBacking.Setting
            ? await SettingStore.RowsAsync(unitOfWork, definition.Key, target, cancellationToken)
            : [];
        BusinessDate? businessDate = definition.HistoryBearing ? asOf ?? await BusinessDateAsync(unitOfWork, target.CompanyId, cancellationToken) : null;

        List<LevelDecision> levels = [];
        string? value = null;
        SettingSource source = SettingSource.Default;
        bool found = false;
        foreach (SettingLevel level in SearchOrder)
        {
            if (!definition.Levels.Contains(level) || !TargetHas(target, level))
            {
                continue;
            }

            IReadOnlyList<SettingValueRecord> atLevel = [.. rows.Where(row => row.Level == level)];
            long version = atLevel.Count == 0 ? 0 : atLevel.Max(row => row.Version);
            SettingValueRecord? deciding = Decide(definition, atLevel, businessDate);
            levels.Add(new LevelDecision(level, version, deciding));
            if (!found && deciding is { Kind: SettingValueKind.Set })
            {
                value = deciding.Value;
                source = Source(level);
                found = true;
            }
        }

        if (!found)
        {
            (value, source) = definition.Backing == SettingBacking.Column && target.CompanyId is { } company
                ? (await ColumnValueAsync(unitOfWork, definition, company, cancellationToken), SettingSource.Column)
                : (definition.DefaultValue, SettingSource.Default);
        }

        levels.Reverse();
        return new Resolved(value, source, levels, target);
    }

    /// <summary>The deciding row of one slot, or null: the rule of Phase 0 Specification, Resolution.</summary>
    public static SettingValueRecord? Decide(SettingDefinition definition, IReadOnlyList<SettingValueRecord> slotRows, BusinessDate? businessDate)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(slotRows);
        if (!definition.HistoryBearing)
        {
            return slotRows.OrderByDescending(row => row.Version).FirstOrDefault();
        }

        BusinessDate date = businessDate ?? throw new ArgumentNullException(nameof(businessDate));
        return slotRows
            .Where(row => row.ValidFrom is { } from && from <= date)
            .OrderByDescending(row => row.ValidFrom!.Value)
            .ThenByDescending(row => row.Version)
            .FirstOrDefault();
    }

    /// <summary>The business date of the company in its time zone, or the UTC date when no company is named (R-TIME-01).</summary>
    public async Task<BusinessDate> BusinessDateAsync(IUnitOfWork unitOfWork, Guid? companyId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (companyId is { } id && await _organisation.GetCompanyAsync(unitOfWork, id, cancellationToken) is { } company)
        {
            return _organisation.BusinessDateFor(company, now);
        }

        return new BusinessDate(DateOnly.FromDateTime(now.UtcDateTime));
    }

    private static async Task<SettingTarget> CompleteAsync(IUnitOfWork unitOfWork, SettingTarget target, CancellationToken cancellationToken)
    {
        if (target.WarehouseId is { } warehouse && target.SiteId is null && await SettingStore.WarehouseSiteAsync(unitOfWork, warehouse, cancellationToken) is { } found)
        {
            return new SettingTarget(target.CompanyId ?? found.CompanyId, found.SiteId, warehouse);
        }

        return target;
    }

    private async Task<string?> ColumnValueAsync(IUnitOfWork unitOfWork, SettingDefinition definition, Guid companyId, CancellationToken cancellationToken)
    {
        CompanyInfo? company = await _organisation.GetCompanyAsync(unitOfWork, companyId, cancellationToken);
        if (company is null)
        {
            return definition.DefaultValue;
        }

        return definition.ColumnName switch
        {
            "company.rounding_mode" => company.RoundingMode.ToString(),
            "company.tax_rounding_level" => company.TaxRoundingLevel.ToString(),
            _ => throw new InvalidOperationException($"Setting {definition.Key} names column {definition.ColumnName}, which the reader does not know."),
        };
    }

    private static bool TargetHas(SettingTarget target, SettingLevel level) => level switch
    {
        SettingLevel.Installation => true,
        SettingLevel.Company => target.CompanyId is not null,
        SettingLevel.Site => target.SiteId is not null,
        _ => target.WarehouseId is not null,
    };

    private static SettingSource Source(SettingLevel level) => level switch
    {
        SettingLevel.Installation => SettingSource.Installation,
        SettingLevel.Company => SettingSource.Company,
        SettingLevel.Site => SettingSource.Site,
        _ => SettingSource.Warehouse,
    };

    /// <summary>The outcome of a resolution: the value and its source, and the levels searched, least specific first.</summary>
    internal sealed record Resolved(string? Value, SettingSource Source, IReadOnlyList<LevelDecision> Levels, SettingTarget Target);
}

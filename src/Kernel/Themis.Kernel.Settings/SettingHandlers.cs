using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Settings;

/// <summary>The codes, names and shared checks of SETTING.SET and SETTING.CLEAR (step 2 brief; D-58, R-SET-01, R-API-02).</summary>
internal static class SettingWrites
{
    public const string SlotVersionIndex = "ux_setting_value_slot_version";
    public const string VersionStaleCode = "version_stale";
    public const string UnknownCode = "setting_unknown";
    public const string NotSettableCode = "setting_not_settable";
    public const string LockedCode = "setting_locked";
    public const string LevelNotAllowedCode = "setting_level_not_allowed";
    public const string ValidFromRequiredCode = "valid_from_required";
    public const string ValidFromNotAllowedCode = "valid_from_not_allowed";
    public const string NothingToClearCode = "setting_value_absent";
    public const string RuleId = "R-SET-01";

    public const string Entity = "SettingValue";
    public const string TypeCode = "SETTING";
    public const string ChangeVerb = "CHANGE";

    /// <summary>Everything a write needs once the definition, the target and the slot's version are checked.</summary>
    internal sealed record Prepared(SettingDefinition Definition, SettingSlot Slot, IReadOnlyList<SettingValueRecord> Rows, long CurrentVersion, SettingValueRecord? Deciding);

    /// <summary>
    /// The checks shared by SET and CLEAR, in order: the key names a definition (400), the definition is backed by a
    /// value row, not locked and allows the level (R-SET-01, 422), valid_from goes with history-bearing definitions
    /// only (400), the target exists in its company (400), and If-Match carries the slot's current version (412, never
    /// stored, R-API-02).
    /// </summary>
    public static async Task<Result<Prepared>> PrepareAsync(SettingDefinitions definitions, SettingsReader reader, ISettingWriteSeam seam, IUnitOfWork unitOfWork,
        string key, SettingLevel level, Guid? companyId, Guid? siteId, Guid? warehouseId, BusinessDate? validFrom, long expectedVersion, CancellationToken cancellationToken)
    {
        SettingDefinition? definition = definitions.Find(key);
        if (definition is null)
        {
            return Error.Validation(UnknownCode, $"Setting {key} is not in the catalogue.", details: Field("key"));
        }

        // R-SET-01: a value is written or cleared only for a Setting-backed definition, never while locked, only at an allowed level.
        if (definition.Backing != SettingBacking.Setting)
        {
            return Error.Rule(RuleId, NotSettableCode, $"Setting {key} is backed by {Backing(definition)}, not by a value; it is changed there, never through SETTING.SET or SETTING.CLEAR.");
        }

        if (definition.LockedReason is { } locked)
        {
            return Error.Rule(RuleId, LockedCode, $"Setting {key} is locked: {locked}");
        }

        if (!definition.Levels.Contains(level))
        {
            return Error.Rule(RuleId, LevelNotAllowedCode, $"Setting {key} may be set at {string.Join(", ", definition.Levels.Order())}, not at {level}.");
        }

        if (definition.HistoryBearing && validFrom is null)
        {
            return Error.Validation(ValidFromRequiredCode, $"Setting {key} is history-bearing; the write names the date it applies from.", details: Field("validFrom"));
        }

        if (!definition.HistoryBearing && validFrom is not null)
        {
            return Error.Validation(ValidFromNotAllowedCode, $"Setting {key} is not history-bearing; a write carries no validFrom.", details: Field("validFrom"));
        }

        Result<SettingSlot> slot = await SlotAsync(unitOfWork, key, level, companyId, siteId, warehouseId, cancellationToken);
        if (slot.IsFailure)
        {
            return Result<Prepared>.Failure(slot.Errors);
        }

        IReadOnlyList<SettingValueRecord> rows = await SettingStore.SlotRowsAsync(unitOfWork, slot.Value, cancellationToken);
        long current = rows.Count == 0 ? 0 : rows.Max(row => row.Version);
        if (expectedVersion != current)
        {
            return Error.PreconditionFailed(VersionStaleCode, $"Setting {key} at {level} is at version {current}, not {expectedVersion}.");
        }

        // The seam of the race test (RR-45): two writes past this point meet the slot's unique version index, and the loser gets 412.
        await seam.AfterVersionCheckAsync(key, level, cancellationToken);
        BusinessDate? businessDate = definition.HistoryBearing ? await reader.BusinessDateAsync(unitOfWork, slot.Value.CompanyId, cancellationToken) : null;
        return new Prepared(definition, slot.Value, rows, current, SettingsReader.Decide(definition, rows, businessDate));
    }

    /// <summary>The slot the level and target name, once the target rows exist in the right company (Conventions, Company scope).</summary>
    private static async Task<Result<SettingSlot>> SlotAsync(IUnitOfWork unitOfWork, string key, SettingLevel level, Guid? companyId, Guid? siteId, Guid? warehouseId, CancellationToken cancellationToken)
    {
        switch (level)
        {
            case SettingLevel.Installation:
                return new SettingSlot(key, level, null, null, null);
            case SettingLevel.Company:
                return await SettingStore.CompanyExistsAsync(unitOfWork, companyId!.Value, cancellationToken)
                    ? new SettingSlot(key, level, companyId, null, null)
                    : Error.Validation("company_unknown", $"Company {companyId} does not exist.", details: Field("companyId"));
            case SettingLevel.Site:
                Guid? siteCompany = await SettingStore.SiteCompanyAsync(unitOfWork, siteId!.Value, cancellationToken);
                if (siteCompany is null)
                {
                    return Error.Validation("site_unknown", $"Site {siteId} does not exist.", details: Field("siteId"));
                }

                return siteCompany == companyId
                    ? new SettingSlot(key, level, companyId, siteId, null)
                    : Error.Validation("site_other_company", $"Site {siteId} belongs to another company.", details: Field("siteId"));
            default:
                (Guid SiteId, Guid CompanyId)? warehouse = await SettingStore.WarehouseSiteAsync(unitOfWork, warehouseId!.Value, cancellationToken);
                if (warehouse is null)
                {
                    return Error.Validation("warehouse_unknown", $"Warehouse {warehouseId} does not exist.", details: Field("warehouseId"));
                }

                return warehouse.Value.CompanyId == companyId
                    ? new SettingSlot(key, level, companyId, null, warehouseId)
                    : Error.Validation("warehouse_other_company", $"Warehouse {warehouseId} belongs to another company.", details: Field("warehouseId"));
        }
    }

    /// <summary>Writes the row, its audit event and its SETTING_CHANGED event, and returns the view (R-AUD-01, D-44, D-50).</summary>
    public static async Task<SettingValueView> WriteAsync(ICommandContext context, Prepared prepared, SettingValueKind kind, string? value, BusinessDate? validFrom, CancellationToken cancellationToken)
    {
        SettingSlot slot = prepared.Slot;
        SettingValueRecord row = new(EntityId.New(), slot.Level, slot.CompanyId, slot.SiteId, slot.WarehouseId, kind, value, validFrom, prepared.CurrentVersion + 1, context.Now);
        await SettingStore.InsertAsync(context.UnitOfWork, slot, row, context.Principal.UserId.Value, context.CommandId.Value, cancellationToken);

        string? previous = prepared.Deciding is { Kind: SettingValueKind.Set } deciding ? deciding.Value : null;
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal)
        {
            ["key"] = new(null, slot.Key),
            ["level"] = new(null, slot.Level.ToString()),
            ["companyId"] = new(null, slot.CompanyId?.ToString()),
            ["siteId"] = new(null, slot.SiteId?.ToString()),
            ["warehouseId"] = new(null, slot.WarehouseId?.ToString()),
            ["kind"] = new(null, kind.ToString()),
            ["value"] = new(previous, value),
            ["validFrom"] = new(null, validFrom?.ToString()),
            ["version"] = new(SettingStore.Text(prepared.CurrentVersion), SettingStore.Text(row.Version)),
        };
        context.Audit.Record(Entity, row.Id, changes);
        // D-50: the aggregate is the value row; an Installation value carries no company in its envelope.
        context.Outbox.Publish(EventNames.Generated(TypeCode, ChangeVerb), EventNames.DefaultSchemaVersion, TypeCode, row.Id, slot.CompanyId,
            new SettingChanged(slot.Key, slot.Level, slot.CompanyId, slot.SiteId, slot.WarehouseId, kind, previous, value, validFrom, row.Version));
        return new SettingValueView(row.Id, slot.Key, slot.Level, slot.CompanyId, slot.SiteId, slot.WarehouseId, kind, value, validFrom, row.Version, row.RecordedAt);
    }

    /// <summary>The SETTING_CHANGED payload (Phase 0 Specification, Commands and query): key, level, target, previous and new value.</summary>
    internal sealed record SettingChanged(string Key, SettingLevel Level, Guid? CompanyId, Guid? SiteId, Guid? WarehouseId, SettingValueKind Kind, string? PreviousValue, string? NewValue, BusinessDate? ValidFrom, long Version);

    private static string Backing(SettingDefinition definition) => definition.Backing switch
    {
        SettingBacking.Column => $"the column {definition.ColumnName}",
        SettingBacking.Data => "rows of an entity",
        _ => $"a planned feature ({definition.Phase})",
    };

    public static Dictionary<string, string> Field(string field) => new(StringComparer.Ordinal) { ["field"] = field };

    /// <summary>Validation before the transaction opens (RR-51): the key's form and the target columns the level needs, never a lookup.</summary>
    public static IEnumerable<Error> ValidateShape(string key, SettingLevel level, Guid? companyId, Guid? siteId, Guid? warehouseId)
    {
        if (!SettingKeys.IsValid(key))
        {
            yield return Error.Validation("setting_key_invalid", $"A setting key is lower case words joined by '_' with at least one '.', at most {SettingKeys.MaxLength} characters.", details: Field("key"));
        }

        bool consistent = level switch
        {
            SettingLevel.Installation => companyId is null && siteId is null && warehouseId is null,
            SettingLevel.Company => companyId is not null && siteId is null && warehouseId is null,
            SettingLevel.Site => companyId is not null && siteId is not null && warehouseId is null,
            SettingLevel.Warehouse => companyId is not null && siteId is null && warehouseId is not null,
            _ => false,
        };
        if (!consistent)
        {
            yield return Error.Validation("setting_target_invalid", $"A {level} value names {Needs(level)} and nothing else.", details: Field("level"));
        }

        if (companyId == Guid.Empty || siteId == Guid.Empty || warehouseId == Guid.Empty)
        {
            yield return Error.Validation("setting_target_invalid", "A target id is not empty.", details: Field("level"));
        }
    }

    private static string Needs(SettingLevel level) => level switch
    {
        SettingLevel.Installation => "no company, site or warehouse",
        SettingLevel.Company => "its company",
        SettingLevel.Site => "its company and site",
        _ => "its company and warehouse",
    };
}

/// <summary>SETTING.SET (step 2 brief; D-58, R-SET-01): a value row with version current + 1, after the value passed its type and range (400).</summary>
internal sealed class SetSettingHandler : ICommandHandler<SetSetting, SettingValueView>
{
    private readonly SettingDefinitions _definitions;
    private readonly SettingsReader _reader;
    private readonly ISettingWriteSeam _seam;

    public SetSettingHandler(SettingDefinitions definitions, SettingsReader reader, ISettingWriteSeam seam)
    {
        _definitions = definitions;
        _reader = reader;
        _seam = seam;
    }

    public async Task<Result<SettingValueView>> HandleAsync(SetSetting command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        Result<SettingWrites.Prepared> prepared = await SettingWrites.PrepareAsync(_definitions, _reader, _seam, context.UnitOfWork, command.Key, command.Level, command.CompanyId, command.SiteId, command.WarehouseId, command.ValidFrom, command.ExpectedVersion, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result<SettingValueView>.Failure(prepared.Errors);
        }

        Result<string> value = SettingValueFormat.Normalise(prepared.Value.Definition, command.Value);
        if (value.IsFailure)
        {
            return Result<SettingValueView>.Failure(value.Errors);
        }

        return await SettingWrites.WriteAsync(context, prepared.Value, SettingValueKind.Set, value.Value, command.ValidFrom, cancellationToken);
    }
}

/// <summary>SETTING.CLEAR (step 2 brief; D-58): a Cleared row at the slot, which has at least one row to clear (400 otherwise).</summary>
internal sealed class ClearSettingHandler : ICommandHandler<ClearSetting, SettingValueView>
{
    private readonly SettingDefinitions _definitions;
    private readonly SettingsReader _reader;
    private readonly ISettingWriteSeam _seam;

    public ClearSettingHandler(SettingDefinitions definitions, SettingsReader reader, ISettingWriteSeam seam)
    {
        _definitions = definitions;
        _reader = reader;
        _seam = seam;
    }

    public async Task<Result<SettingValueView>> HandleAsync(ClearSetting command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        Result<SettingWrites.Prepared> prepared = await SettingWrites.PrepareAsync(_definitions, _reader, _seam, context.UnitOfWork, command.Key, command.Level, command.CompanyId, command.SiteId, command.WarehouseId, command.ValidFrom, command.ExpectedVersion, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result<SettingValueView>.Failure(prepared.Errors);
        }

        if (prepared.Value.Rows.Count == 0)
        {
            return Error.Validation(SettingWrites.NothingToClearCode, $"Setting {command.Key} has no value at {command.Level} for this target; there is nothing to clear.");
        }

        return await SettingWrites.WriteAsync(context, prepared.Value, SettingValueKind.Cleared, null, command.ValidFrom, cancellationToken);
    }
}

internal sealed class SetSettingValidator : ICommandValidator<SetSetting>
{
    public IEnumerable<Error> Validate(SetSetting command)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (Error error in SettingWrites.ValidateShape(command.Key, command.Level, command.CompanyId, command.SiteId, command.WarehouseId))
        {
            yield return error;
        }

        if (string.IsNullOrWhiteSpace(command.Value))
        {
            yield return Error.Validation("setting_value_required", "A value is required.", details: SettingWrites.Field("value"));
        }
    }
}

internal sealed class ClearSettingValidator : ICommandValidator<ClearSetting>
{
    public IEnumerable<Error> Validate(ClearSetting command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SettingWrites.ValidateShape(command.Key, command.Level, command.CompanyId, command.SiteId, command.WarehouseId);
    }
}

/// <summary>SETTING.READ: the effective value at the target with its source, and the current version of each allowed level there (D-47).</summary>
internal sealed class GetSettingHandler : IQueryHandler<GetSetting, SettingReadView>
{
    private readonly SettingDefinitions _definitions;
    private readonly SettingsReader _reader;

    public GetSettingHandler(SettingDefinitions definitions, SettingsReader reader)
    {
        _definitions = definitions;
        _reader = reader;
    }

    public async Task<Result<SettingReadView>> HandleAsync(GetSetting query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;
        SettingDefinition? definition = _definitions.Find(query.Key);
        if (definition is null)
        {
            return Error.NotFound(SettingWrites.UnknownCode, $"Setting {query.Key} is not in the catalogue.");
        }

        if (query.CompanyId is { } company && !await SettingStore.CompanyExistsAsync(unitOfWork, company, cancellationToken))
        {
            return Error.NotFound("company_not_found", $"Company {company} does not exist.");
        }

        if (query.SiteId is { } site && await SettingStore.SiteCompanyAsync(unitOfWork, site, cancellationToken) != query.CompanyId)
        {
            return Error.NotFound("site_not_found", $"Site {site} does not exist in company {query.CompanyId}.");
        }

        if (query.WarehouseId is { } warehouse && (await SettingStore.WarehouseSiteAsync(unitOfWork, warehouse, cancellationToken))?.CompanyId != query.CompanyId)
        {
            return Error.NotFound("warehouse_not_found", $"Warehouse {warehouse} does not exist in company {query.CompanyId}.");
        }

        SettingsReader.Resolved resolved = await _reader.ResolveAsync(unitOfWork, definition, new SettingTarget(query.CompanyId, query.SiteId, query.WarehouseId), asOf: null, cancellationToken);
        return new SettingReadView(
            definition.Key,
            definition.ValueType,
            definition.Backing,
            definition.DefaultValue,
            definition.LockedReason,
            [.. definition.Levels.Order()],
            resolved.Value,
            resolved.Source,
            [.. resolved.Levels.Select(level => new SettingLevelState(level.Level, level.Version, level.Deciding?.Kind, level.Deciding?.Value, level.Deciding?.ValidFrom))]);
    }
}

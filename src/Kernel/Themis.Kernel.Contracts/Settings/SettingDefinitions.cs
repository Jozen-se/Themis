using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Themis.Kernel.Contracts.Settings;

/// <summary>Phase 0 Specification, SettingDefinition.levels: the levels a value may be written at, most specific last (R-SET-01).</summary>
public enum SettingLevel
{
    Installation,
    Company,
    Site,
    Warehouse,
}

/// <summary>
/// Phase 0 Specification, SettingDefinition.backing: where a configurable default lives. Setting: a value row. Column: a
/// named column of an entity, changed only by that entity's command. Data: rows of an entity, such as the shipped
/// conflict pairs. Planned: the feature that reads it comes in a later phase (R-SET-01, D-58).
/// </summary>
public enum SettingBacking
{
    Setting,
    Column,
    Data,
    Planned,
}

/// <summary>Phase 0 Specification, SettingDefinition.value_type; decimals travel as strings (R-MON-01).</summary>
#pragma warning disable CA1720 // the specification names the value types; the stored text is the member name
public enum SettingValueType
{
    Integer,
    Decimal,
    Boolean,
    Code,
    Duration,
}
#pragma warning restore CA1720

/// <summary>
/// One definition of the settings catalogue (Phase 0 Specification, Settings entities; D-58): the key, its type and
/// validation, the documented default, the levels a value may be written at, whether the caller dates its values,
/// the lock, the backing, and the rule and decision that make it a configurable default (P-11, D-29). The catalogue
/// in code is the source; Themis.Migrator writes it to kernel.setting_definition on every run. A Planned definition
/// names its phase and, as text, the levels that phase adds (for example Customer or Item), which the level enum
/// does not hold yet. Setting and Column definitions have a value type; Data and Planned definitions have no value
/// type, default or validation (D-63): a Planned one gets them from the specification of the phase that builds it,
/// and a Data one is the rows of its entity.
/// </summary>
public sealed record SettingDefinition(
    string Key,
    SettingValueType? ValueType,
    string? DefaultValue,
    IReadOnlySet<SettingLevel> Levels,
    SettingBacking Backing,
    string RuleId,
    string Description,
    string? DecisionId = null,
    string? Min = null,
    string? Max = null,
    IReadOnlyList<string>? AllowedValues = null,
    bool HistoryBearing = false,
    string? LockedReason = null,
    string? ColumnName = null,
    string? Phase = null,
    string? PlannedLevels = null)
{
    public string Key { get; } = SettingKeys.Validated(Key);

    /// <summary>A value may be written only for a Setting backing that is not locked (R-SET-01).</summary>
    public bool IsWritable => Backing == SettingBacking.Setting && LockedReason is null;

    /// <summary>
    /// D-63: whether the definition has the shape its backing allows: Setting and Column carry a value type; Data and
    /// Planned carry none, and no default, minimum, maximum or allowed values. The database checks the same rule.
    /// </summary>
    public bool HasShapeOfItsBacking => Backing is SettingBacking.Setting or SettingBacking.Column
        ? ValueType is not null
        : ValueType is null && DefaultValue is null && Min is null && Max is null && AllowedValues is null;
}

/// <summary>The keys of the definitions milestone 2 reads in code (Phase 0 Specification, Definitions in milestone 2); the catalogue holds the rest.</summary>
public static partial class SettingKeys
{
    /// <summary>Days an idempotency record lives after it was written (R-API-01; the first setting, D-45 ends).</summary>
    public const string IdempotencyRetentionDays = "idempotency.retention_days";

    /// <summary>The most lines a document may hold (RR-54).</summary>
    public const string DocumentMaxLines = "document.max_lines";

    /// <summary>Whether a warehouse may go below zero on hand (R-INV-03, D-09); locked until a costing rule for negative quantities exists.</summary>
    public const string InventoryNegativeStockAllowed = "inventory.negative_stock_allowed";

    /// <summary>Company.rounding_mode, a column-backed default (R-MON-02).</summary>
    public const string CompanyRoundingMode = "company.rounding_mode";

    /// <summary>Company.tax_rounding_level, a column-backed default set by D-10 (R-MON-02).</summary>
    public const string CompanyTaxRoundingLevel = "company.tax_rounding_level";

    public const int MaxLength = 100;

    [GeneratedRegex("^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+\\z")]
    private static partial Regex Pattern();

    /// <summary>Unique, dotted, lower case: an area, a dot, a name (Phase 0 Specification, SettingDefinition.key).</summary>
    public static bool IsValid([NotNullWhen(true)] string? key) => key is not null && key.Length <= MaxLength && Pattern().IsMatch(key);

    public static string Validated(string key) =>
        IsValid(key) ? key : throw new ArgumentException($"'{key}' is not a setting key: lower case words joined by '_' and at least one '.', at most {MaxLength} characters.", nameof(key));
}

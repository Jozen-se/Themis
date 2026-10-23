namespace Themis.Kernel.Persistence.Entities.Settings;

/// <summary>
/// Phase 0 Specification, SettingDefinition: the catalogue row Themis.Migrator writes from code on every run (D-58).
/// Sets and lists are stored as text, comma-separated, in the order the catalogue gives them.
/// </summary>
internal sealed class SettingDefinitionRow
{
    public required Guid Id { get; init; }

    public required string Key { get; init; }

    /// <summary>Empty for Data and Planned definitions (D-63); a check holds the shape to the backing.</summary>
    public string? ValueType { get; set; }

    public string? DefaultValue { get; set; }

    public string? Min { get; set; }

    public string? Max { get; set; }

    public string? AllowedValues { get; set; }

    public required string Levels { get; set; }

    public required bool HistoryBearing { get; set; }

    public string? LockedReason { get; set; }

    public required string Backing { get; set; }

    public string? ColumnName { get; set; }

    public string? Phase { get; set; }

    public string? PlannedLevels { get; set; }

    public required string RuleId { get; set; }

    public string? DecisionId { get; set; }

    public required string Description { get; set; }

    /// <summary>True once the definition has left the catalogue in code; the row stays because values reference it (D-58).</summary>
    public required bool Retired { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Phase 0 Specification, SettingValue: insert-only; a SET or CLEAR inserts one row with the slot's next version (D-58).</summary>
internal sealed class SettingValueRow
{
    public required Guid Id { get; init; }

    public required string DefinitionKey { get; init; }

    public required string Level { get; init; }

    public Guid? CompanyId { get; init; }

    public Guid? SiteId { get; init; }

    public Guid? WarehouseId { get; init; }

    public required string Kind { get; init; }

    public string? Value { get; init; }

    public DateOnly? ValidFrom { get; init; }

    public required long Version { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required Guid RecordedBy { get; init; }

    public required Guid CommandId { get; init; }
}

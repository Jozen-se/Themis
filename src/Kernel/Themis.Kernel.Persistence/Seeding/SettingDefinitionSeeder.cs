using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Persistence.Entities;
using Themis.Kernel.Persistence.Entities.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Persistence.Seeding;

/// <summary>What a catalogue run found: rows inserted or aligned, definitions retired, and the warnings the migrator prints (D-58).</summary>
internal sealed record SettingCatalogReport(int Changed, int Retired, IReadOnlyList<string> Warnings);

/// <summary>
/// Writes the settings catalogue from code to kernel.setting_definition as themis_migrator, on every run of
/// Themis.Migrator, in one transaction (D-58; like the document type registry, D-40): a missing definition is inserted,
/// a row whose attributes differ is brought in line, and a row whose definition has left the catalogue is marked
/// retired and reported, never deleted, because values reference it. A stored value that a tightened range left
/// outside its definition is reported and never changed. Every row written gets one audit event under the system
/// user (R-AUD-01); no outbox event, because no command ran.
/// </summary>
internal static class SettingDefinitionSeeder
{
    public const string CommandName = IdentitySeeder.CatalogueCommandName;

    private static readonly JsonSerializerOptions ChangesJson = new(JsonSerializerDefaults.Web);

    public static async Task<SettingCatalogReport> ApplyCatalogueAsync(string migratorConnectionString, IReadOnlyList<SettingDefinition> catalogue, Func<SettingDefinition, string?, bool> isValidValue, DateTimeOffset now, UserId seededBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(isValidValue);
        await using KernelDbContext context = KernelDbContextFactory.ForMigrations(migratorConnectionString);
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        Guid by = seededBy.Value;
        // D-51: one command id per seeder call, shared by every audit event it writes.
        Guid commandId = EntityId.New();
        int changed = 0;
        int retired = 0;
        List<string> warnings = [];

        Dictionary<string, SettingDefinitionRow> rows = await context.SettingDefinitions.ToDictionaryAsync(row => row.Key, StringComparer.Ordinal, cancellationToken);
        foreach (SettingDefinition definition in catalogue)
        {
            Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
            if (!rows.TryGetValue(definition.Key, out SettingDefinitionRow? existing))
            {
                existing = new SettingDefinitionRow
                {
                    Id = EntityId.New(),
                    Key = definition.Key,
                    ValueType = definition.ValueType.ToString(),
                    DefaultValue = definition.DefaultValue,
                    Min = definition.Min,
                    Max = definition.Max,
                    AllowedValues = Join(definition.AllowedValues),
                    Levels = Levels(definition),
                    HistoryBearing = definition.HistoryBearing,
                    LockedReason = definition.LockedReason,
                    Backing = definition.Backing.ToString(),
                    ColumnName = definition.ColumnName,
                    Phase = definition.Phase,
                    PlannedLevels = definition.PlannedLevels,
                    RuleId = definition.RuleId,
                    DecisionId = definition.DecisionId,
                    Description = definition.Description,
                    Retired = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                context.SettingDefinitions.Add(existing);
                rows[definition.Key] = existing;
                foreach ((string field, string? value) in Fields(existing))
                {
                    changes[field] = new(null, value);
                }
            }
            else
            {
                Align(changes, "valueType", existing.ValueType, definition.ValueType.ToString(), value => existing.ValueType = value!);
                Align(changes, "defaultValue", existing.DefaultValue, definition.DefaultValue, value => existing.DefaultValue = value);
                Align(changes, "min", existing.Min, definition.Min, value => existing.Min = value);
                Align(changes, "max", existing.Max, definition.Max, value => existing.Max = value);
                Align(changes, "allowedValues", existing.AllowedValues, Join(definition.AllowedValues), value => existing.AllowedValues = value);
                Align(changes, "levels", existing.Levels, Levels(definition), value => existing.Levels = value!);
                Align(changes, "historyBearing", Text(existing.HistoryBearing), Text(definition.HistoryBearing), value => existing.HistoryBearing = value == "True");
                Align(changes, "lockedReason", existing.LockedReason, definition.LockedReason, value => existing.LockedReason = value);
                Align(changes, "backing", existing.Backing, definition.Backing.ToString(), value => existing.Backing = value!);
                Align(changes, "columnName", existing.ColumnName, definition.ColumnName, value => existing.ColumnName = value);
                Align(changes, "phase", existing.Phase, definition.Phase, value => existing.Phase = value);
                Align(changes, "plannedLevels", existing.PlannedLevels, definition.PlannedLevels, value => existing.PlannedLevels = value);
                Align(changes, "ruleId", existing.RuleId, definition.RuleId, value => existing.RuleId = value!);
                Align(changes, "decisionId", existing.DecisionId, definition.DecisionId, value => existing.DecisionId = value);
                Align(changes, "description", existing.Description, definition.Description, value => existing.Description = value!);
                Align(changes, "retired", Text(existing.Retired), Text(false), value => existing.Retired = value == "True");
                if (changes.Count > 0)
                {
                    existing.UpdatedAt = now;
                }
            }

            if (changes.Count > 0)
            {
                Audit(context, commandId, "SettingDefinition", existing.Id, now, by, changes);
                changed++;
            }

            // A range tightened in code leaves stored values where they are; the owner hears about them here.
            string key = definition.Key;
            List<SettingValueRow> values = await context.SettingValues.AsNoTracking().Where(value => value.DefinitionKey == key && value.Kind == "Set").ToListAsync(cancellationToken);
            foreach (SettingValueRow value in values.Where(value => !isValidValue(definition, value.Value)))
            {
                warnings.Add($"Setting {key} holds the value '{value.Value}' at {value.Level} {Target(value)} (version {value.Version}), outside its definition's range; the value stays and the owner decides.");
            }
        }

        // A definition that left the catalogue stays, retired, because values reference it (D-58).
        HashSet<string> catalogued = catalogue.Select(definition => definition.Key).ToHashSet(StringComparer.Ordinal);
        foreach (SettingDefinitionRow orphan in rows.Values.Where(row => !catalogued.Contains(row.Key) && !row.Retired))
        {
            orphan.Retired = true;
            orphan.UpdatedAt = now;
            Audit(context, commandId, "SettingDefinition", orphan.Id, now, by, new Dictionary<string, FieldChange> { ["retired"] = new(Text(false), Text(true)) });
            string orphanKey = orphan.Key;
            int referencing = await context.SettingValues.CountAsync(value => value.DefinitionKey == orphanKey, cancellationToken);
            warnings.Add($"Setting {orphanKey} has left the catalogue; its definition row is marked retired and its {referencing} value row(s) stay.");
            retired++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SettingCatalogReport(changed, retired, warnings);
    }

    private static IEnumerable<(string Field, string? Value)> Fields(SettingDefinitionRow row) =>
    [
        ("key", row.Key), ("valueType", row.ValueType), ("defaultValue", row.DefaultValue), ("min", row.Min), ("max", row.Max),
        ("allowedValues", row.AllowedValues), ("levels", row.Levels), ("historyBearing", Text(row.HistoryBearing)), ("lockedReason", row.LockedReason),
        ("backing", row.Backing), ("columnName", row.ColumnName), ("phase", row.Phase), ("plannedLevels", row.PlannedLevels), ("ruleId", row.RuleId),
        ("decisionId", row.DecisionId), ("description", row.Description), ("retired", Text(row.Retired)),
    ];

    private static string Target(SettingValueRow value) =>
        value.Level switch
        {
            "Company" => $"company {value.CompanyId}",
            "Site" => $"site {value.SiteId}",
            "Warehouse" => $"warehouse {value.WarehouseId}",
            _ => "installation",
        };

    private static string? Join(IEnumerable<string>? values) => values is null ? null : string.Join(",", values);

    private static string Levels(SettingDefinition definition) => string.Join(",", definition.Levels.Order().Select(level => level.ToString()));

    private static string Text(bool value) => value ? "True" : "False";

    private static void Align(Dictionary<string, FieldChange> changes, string field, string? before, string? after, Action<string?> apply)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            apply(after);
            changes[field] = new(before, after);
        }
    }

    private static void Audit(KernelDbContext context, Guid commandId, string entityType, Guid entityId, DateTimeOffset now, Guid by, IReadOnlyDictionary<string, FieldChange> changes) =>
        context.AuditEvents.Add(new AuditEvent
        {
            Id = EntityId.New(),
            CommandId = commandId,
            OccurredAt = now,
            UserId = by,
            CommandName = CommandName,
            EntityType = entityType,
            EntityId = entityId,
            Changes = JsonSerializer.Serialize(changes, ChangesJson),
        });

    internal static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}

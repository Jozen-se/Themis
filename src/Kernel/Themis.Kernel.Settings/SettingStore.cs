using System.Data.Common;
using System.Globalization;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Settings;

/// <summary>One stored row of kernel.setting_value as the reader and the commands see it.</summary>
internal sealed record SettingValueRecord(Guid Id, SettingLevel Level, Guid? CompanyId, Guid? SiteId, Guid? WarehouseId, SettingValueKind Kind, string? Value, BusinessDate? ValidFrom, long Version, DateTimeOffset RecordedAt);

/// <summary>The identity of one slot: a definition, a level and the target the level needs (Phase 0 Specification, SettingValue.version).</summary>
internal readonly record struct SettingSlot(string Key, SettingLevel Level, Guid? CompanyId, Guid? SiteId, Guid? WarehouseId);

/// <summary>
/// The SQL of the settings framework, every statement as themis_kernel (R-DB-01): the rows of a key at a target, the
/// current version of a slot, the insert of a row (the only write; the role holds no UPDATE or DELETE, R-SET-01), and
/// the organisation lookups the commands and the reader need.
/// </summary>
internal static class SettingStore
{
    private const string Columns = "id, level, company_id, site_id, warehouse_id, kind, value, valid_from, version, recorded_at";

    /// <summary>Every row of the key at the installation and at the target's company, site and warehouse, newest version first.</summary>
    public static Task<IReadOnlyList<SettingValueRecord>> RowsAsync(IUnitOfWork unitOfWork, string key, SettingTarget target, CancellationToken cancellationToken)
    {
        List<string> clauses = ["level = 'Installation'"];
        Dictionary<string, object?> parameters = new(StringComparer.Ordinal) { ["key"] = key };
        if (target.CompanyId is { } company)
        {
            clauses.Add("(level = 'Company' AND company_id = @company)");
            parameters["company"] = company;
        }

        if (target.SiteId is { } site)
        {
            clauses.Add("(level = 'Site' AND site_id = @site)");
            parameters["site"] = site;
        }

        if (target.WarehouseId is { } warehouse)
        {
            clauses.Add("(level = 'Warehouse' AND warehouse_id = @warehouse)");
            parameters["warehouse"] = warehouse;
        }

        string sql = $"SELECT {Columns} FROM kernel.setting_value WHERE definition_key = @key AND ({string.Join(" OR ", clauses)}) ORDER BY level, version DESC";
        return unitOfWork.QueryAsync(DatabaseRole.Kernel, sql, parameters, Map, cancellationToken);
    }

    /// <summary>The rows of one slot, newest version first.</summary>
    public static Task<IReadOnlyList<SettingValueRecord>> SlotRowsAsync(IUnitOfWork unitOfWork, SettingSlot slot, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"SELECT {Columns} FROM kernel.setting_value WHERE definition_key = @key AND level = @level AND company_id IS NOT DISTINCT FROM @company AND site_id IS NOT DISTINCT FROM @site AND warehouse_id IS NOT DISTINCT FROM @warehouse ORDER BY version DESC",
            SlotParameters(slot), Map, cancellationToken);

    /// <summary>Inserts the row; the unique slot index refuses a version already taken (R-API-02, mapped to 412 by the pipeline, RR-45).</summary>
    public static async Task InsertAsync(IUnitOfWork unitOfWork, SettingSlot slot, SettingValueRecord row, Guid recordedBy, Guid commandId, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = SlotParameters(slot);
        parameters["id"] = row.Id;
        parameters["kind"] = row.Kind.ToString();
        parameters["value"] = row.Value;
        parameters["validFrom"] = row.ValidFrom?.Value;
        parameters["version"] = row.Version;
        parameters["recordedAt"] = row.RecordedAt;
        parameters["recordedBy"] = recordedBy;
        parameters["commandId"] = commandId;
        int inserted = await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "INSERT INTO kernel.setting_value (id, definition_key, level, company_id, site_id, warehouse_id, kind, value, valid_from, version, recorded_at, recorded_by, command_id) " +
            "VALUES (@id, @key, @level, @company, @site, @warehouse, @kind, @value, @validFrom, @version, @recordedAt, @recordedBy, @commandId)",
            parameters, cancellationToken);
        if (inserted != 1)
        {
            throw new InvalidOperationException($"Setting value {row.Id} was not inserted ({inserted} rows).");
        }
    }

    /// <summary>The site and company of a warehouse, or null when it does not exist.</summary>
    public static async Task<(Guid SiteId, Guid CompanyId)?> WarehouseSiteAsync(IUnitOfWork unitOfWork, Guid warehouseId, CancellationToken cancellationToken)
    {
        IReadOnlyList<(Guid, Guid)> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, "SELECT site_id, company_id FROM kernel.warehouse WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = warehouseId }, reader => (reader.GetGuid(0), reader.GetGuid(1)), cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>The company of a site, or null when it does not exist.</summary>
    public static async Task<Guid?> SiteCompanyAsync(IUnitOfWork unitOfWork, Guid siteId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, "SELECT company_id FROM kernel.site WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = siteId }, reader => reader.GetGuid(0), cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>Whether the company exists.</summary>
    public static async Task<bool> CompanyExistsAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT 1 FROM kernel.company WHERE id = @id", new Dictionary<string, object?> { ["id"] = companyId }, cancellationToken) is not null;

    private static Dictionary<string, object?> SlotParameters(SettingSlot slot) => new(StringComparer.Ordinal)
    {
        ["key"] = slot.Key,
        ["level"] = slot.Level.ToString(),
        ["company"] = slot.CompanyId,
        ["site"] = slot.SiteId,
        ["warehouse"] = slot.WarehouseId,
    };

    private static SettingValueRecord Map(DbDataReader reader) => new(
        reader.GetGuid(0),
        Enum.Parse<SettingLevel>(reader.GetString(1)),
        reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetGuid(3),
        reader.IsDBNull(4) ? null : reader.GetGuid(4),
        Enum.Parse<SettingValueKind>(reader.GetString(5)),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : new BusinessDate(reader.GetFieldValue<DateOnly>(7)),
        reader.GetInt64(8),
        reader.GetFieldValue<DateTimeOffset>(9));

    public static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}

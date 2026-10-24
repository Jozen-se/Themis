using System.Data.Common;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>A party row as the company commands read and write it (D-64): the company's own party.</summary>
internal sealed record PartyRow(
    Guid Id,
    string PartyNumber,
    PartyType PartyType,
    string Name,
    string? OrganisationNumber,
    string? VatNumber,
    CountryCode CountryCode,
    string? Language,
    MasterDataStatus Status,
    string DuplicateKey,
    long Version);

/// <summary>
/// Raw SQL on the command's unit of work as themis_kernel (R-DB-01) for the organisation commands and queries of
/// milestone 2 step 3: reads, existence checks, inserts, versioned updates (lock class 1, R-API-02) and the trigger
/// checks of R-MD-06. The kernel data context stays in Themis.Kernel.Persistence; this capability reaches the tables
/// through the unit of work only (ARCH-08). Nothing here deletes a row (R-MD-03).
/// </summary>
internal static class OrganisationStore
{
    private const string SiteColumns = "id, company_id, code, name, time_zone, version";
    private const string WarehouseColumns = "id, site_id, company_id, code, name, is_default, status, version";
    private const string ChartColumns = "id, code, name, template, version";
    private const string PartyColumns = "id, party_number, party_type, name, organisation_number, vat_number, country_code, language, status, duplicate_key, version";

    private static Dictionary<string, object?> Id(Guid id) => new() { ["id"] = id };

    /// <summary>The optional company of a lookup by id as a clause and its parameters: a null parameter has no type for PostgreSQL to resolve (42P08).</summary>
    private static (string Clause, Dictionary<string, object?> Parameters) IdInCompany(Guid id, Guid? companyId)
    {
        Dictionary<string, object?> parameters = Id(id);
        if (companyId is not { } company)
        {
            return (string.Empty, parameters);
        }

        parameters["company"] = company;
        return (" AND company_id = @company", parameters);
    }

    // ---- company ---------------------------------------------------------------------------------------------------

    public static async Task<CompanyInfo?> FindCompanyAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<CompanyInfo> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {OrganisationReader.CompanyColumns} FROM kernel.company WHERE id = @id", Id(id), OrganisationReader.ReadCompany, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public static async Task<bool> CompanyCodeExistsAsync(IUnitOfWork unitOfWork, Code code, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.company WHERE code = @code", new Dictionary<string, object?> { ["code"] = code.Value }, cancellationToken) is long count && count > 0;

    /// <summary>R-MD-05, D-64: whether another company of the country carries the normalised organisation number.</summary>
    public static async Task<bool> OrganisationNumberTakenAsync(IUnitOfWork unitOfWork, CountryCode country, string normalised, Guid? exceptCompanyId, CancellationToken cancellationToken)
    {
        // An optional uuid is a clause, not a parameter: a null parameter has no type for PostgreSQL to resolve (42P08).
        Dictionary<string, object?> parameters = new() { ["country"] = country.Value, ["number"] = normalised };
        string except = string.Empty;
        if (exceptCompanyId is { } exceptId)
        {
            parameters["except"] = exceptId;
            except = " AND id <> @except";
        }

        return await unitOfWork.ScalarAsync(DatabaseRole.Kernel, $"SELECT count(*) FROM kernel.company WHERE country_code = @country AND organisation_number_normalised = @number{except}", parameters, cancellationToken) is long count && count > 0;
    }

    public static async Task<bool> CurrencyExistsAsync(IUnitOfWork unitOfWork, CurrencyCode currency, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.currency WHERE code = @code", new Dictionary<string, object?> { ["code"] = currency.Value }, cancellationToken) is long count && count > 0;

    /// <summary>The installation's default locale, the language of the company's own party; null when no installation row exists yet (the bootstrap of step 13 creates it).</summary>
    public static async Task<string?> InstallationLocaleAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT default_locale FROM kernel.installation ORDER BY created_at LIMIT 1", null, cancellationToken) as string;

    public static Task InsertCompanyAsync(IUnitOfWork unitOfWork, CompanyInfo company, string organisationNumberNormalised, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.company (id, code, party_id, legal_name, organisation_number, organisation_number_normalised, vat_number, country_code, base_currency, time_zone, fiscal_year_start_month, chart_of_accounts_id, rounding_mode, tax_rounding_level, status, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @code, @party, @legalName, @organisationNumber, @normalised, @vatNumber, @country, @currency, @timeZone, @month, @chart, @rounding, @taxRounding, @status, @now, @by, @now, @by, @version)
            """,
            CompanyParameters(company, organisationNumberNormalised, now, by),
            cancellationToken);

    /// <summary>
    /// Lock class 1 (R-LED-08): the versioned update of the company row, which waits behind a posting's FOR SHARE of
    /// class 1b (D-65); 0 rows means the version is stale (R-API-02). The R-MD-06 checks run after it in their own
    /// statements, on the snapshot taken after the wait.
    /// </summary>
    public static async Task<int> UpdateCompanyAsync(IUnitOfWork unitOfWork, CompanyInfo company, string organisationNumberNormalised, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = CompanyParameters(company, organisationNumberNormalised, now, by);
        parameters["expected"] = expectedVersion;
        return await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            UPDATE kernel.company
            SET legal_name = @legalName, organisation_number = @organisationNumber, organisation_number_normalised = @normalised, vat_number = @vatNumber, country_code = @country,
                base_currency = @currency, time_zone = @timeZone, fiscal_year_start_month = @month, chart_of_accounts_id = @chart, rounding_mode = @rounding, tax_rounding_level = @taxRounding,
                updated_at = @now, updated_by = @by, version = @version
            WHERE id = @id AND version = @expected
            """,
            parameters,
            cancellationToken);
    }

    /// <summary>D-65: the company's first journal entry or inventory entry fixes base currency, time zone, country, organisation number and rounding mode.</summary>
    public static async Task<bool> CompanyHasPostingsAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel,
            "SELECT EXISTS (SELECT 1 FROM kernel.journal_entry WHERE company_id = @id) OR EXISTS (SELECT 1 FROM kernel.inventory_entry WHERE company_id = @id)",
            Id(companyId), cancellationToken) is true;

    /// <summary>D-65: the company's first posting profile rule fixes its chart of accounts (its first posting does too).</summary>
    public static async Task<bool> CompanyHasPostingProfileRulesAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT EXISTS (SELECT 1 FROM kernel.posting_profile_rule WHERE company_id = @id)", Id(companyId), cancellationToken) is true;

    /// <summary>D-65: the company's first fiscal year fixes its fiscal year start month.</summary>
    public static async Task<bool> CompanyHasFiscalYearsAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT EXISTS (SELECT 1 FROM kernel.fiscal_year WHERE company_id = @id)", Id(companyId), cancellationToken) is true;

    private static Dictionary<string, object?> CompanyParameters(CompanyInfo company, string organisationNumberNormalised, DateTimeOffset now, UserId by) => new()
    {
        ["id"] = company.Id,
        ["code"] = company.Code.Value,
        ["party"] = company.PartyId,
        ["legalName"] = company.LegalName,
        ["organisationNumber"] = company.OrganisationNumber,
        ["normalised"] = organisationNumberNormalised,
        ["vatNumber"] = company.VatNumber,
        ["country"] = company.CountryCode.Value,
        ["currency"] = company.BaseCurrency.Value,
        ["timeZone"] = company.TimeZone,
        ["month"] = company.FiscalYearStartMonth,
        ["chart"] = company.ChartOfAccountsId,
        ["rounding"] = company.RoundingMode.ToString(),
        ["taxRounding"] = company.TaxRoundingLevel.ToString(),
        ["status"] = company.Status.ToString(),
        ["now"] = now,
        ["by"] = by.Value,
        ["version"] = company.Version,
    };

    // ---- party (the company's own, D-64) ---------------------------------------------------------------------------

    public static async Task<PartyRow?> FindPartyAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<PartyRow> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {PartyColumns} FROM kernel.party WHERE id = @id", Id(id), ReadParty, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public static Task InsertPartyAsync(IUnitOfWork unitOfWork, PartyRow party, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            INSERT INTO kernel.party (id, party_number, party_type, name, organisation_number, vat_number, country_code, language, status, duplicate_key, created_at, created_by, updated_at, updated_by, version)
            VALUES (@id, @number, @type, @name, @organisationNumber, @vatNumber, @country, @language, @status, @duplicateKey, @now, @by, @now, @by, @version)
            """,
            PartyParameters(party, now, by),
            cancellationToken);

    /// <summary>Lock class 1 continues after the company row (Lock order, 1b): the company's own party follows the shared fields.</summary>
    public static Task<int> UpdatePartyAsync(IUnitOfWork unitOfWork, PartyRow party, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = PartyParameters(party, now, by);
        parameters["expected"] = expectedVersion;
        return unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            """
            UPDATE kernel.party
            SET name = @name, organisation_number = @organisationNumber, vat_number = @vatNumber, country_code = @country, duplicate_key = @duplicateKey, updated_at = @now, updated_by = @by, version = @version
            WHERE id = @id AND version = @expected
            """,
            parameters,
            cancellationToken);
    }

    private static Dictionary<string, object?> PartyParameters(PartyRow party, DateTimeOffset now, UserId by) => new()
    {
        ["id"] = party.Id,
        ["number"] = party.PartyNumber,
        ["type"] = party.PartyType.ToString(),
        ["name"] = party.Name,
        ["organisationNumber"] = party.OrganisationNumber,
        ["vatNumber"] = party.VatNumber,
        ["country"] = party.CountryCode.Value,
        ["language"] = party.Language,
        ["status"] = party.Status.ToString(),
        ["duplicateKey"] = party.DuplicateKey,
        ["now"] = now,
        ["by"] = by.Value,
        ["version"] = party.Version,
    };

    private static PartyRow ReadParty(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        Enum.Parse<PartyType>(reader.GetString(2)),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        CountryCode.From(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        Enum.Parse<MasterDataStatus>(reader.GetString(8)),
        reader.GetString(9),
        reader.GetInt64(10));

    // ---- chart of accounts -----------------------------------------------------------------------------------------

    public static async Task<ChartOfAccountsView?> FindChartAsync(IUnitOfWork unitOfWork, Guid id, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChartOfAccountsView> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {ChartColumns} FROM kernel.chart_of_accounts WHERE id = @id", Id(id), ReadChart, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public static Task<IReadOnlyList<ChartOfAccountsView>> ListChartsAsync(IUnitOfWork unitOfWork, int limit, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {ChartColumns} FROM kernel.chart_of_accounts ORDER BY code LIMIT @limit", new Dictionary<string, object?> { ["limit"] = limit }, ReadChart, cancellationToken);

    public static async Task<bool> ChartCodeExistsAsync(IUnitOfWork unitOfWork, Code code, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.chart_of_accounts WHERE code = @code", new Dictionary<string, object?> { ["code"] = code.Value }, cancellationToken) is long count && count > 0;

    public static Task InsertChartAsync(IUnitOfWork unitOfWork, ChartOfAccountsView chart, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "INSERT INTO kernel.chart_of_accounts (id, code, name, template, created_at, created_by, updated_at, updated_by, version) VALUES (@id, @code, @name, @template, @now, @by, @now, @by, @version)",
            ChartParameters(chart, now, by),
            cancellationToken);

    public static Task<int> UpdateChartAsync(IUnitOfWork unitOfWork, ChartOfAccountsView chart, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = ChartParameters(chart, now, by);
        parameters["expected"] = expectedVersion;
        return unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.chart_of_accounts SET name = @name, template = @template, updated_at = @now, updated_by = @by, version = @version WHERE id = @id AND version = @expected",
            parameters,
            cancellationToken);
    }

    private static Dictionary<string, object?> ChartParameters(ChartOfAccountsView chart, DateTimeOffset now, UserId by) => new()
    {
        ["id"] = chart.Id,
        ["code"] = chart.Code.Value,
        ["name"] = chart.Name,
        ["template"] = chart.Template,
        ["now"] = now,
        ["by"] = by.Value,
        ["version"] = chart.Version,
    };

    private static ChartOfAccountsView ReadChart(DbDataReader reader) =>
        new(reader.GetGuid(0), Code.From(reader.GetString(1)), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt64(4));

    // ---- site --------------------------------------------------------------------------------------------------------

    public static async Task<SiteInfo?> FindSiteAsync(IUnitOfWork unitOfWork, Guid id, Guid? companyId, CancellationToken cancellationToken)
    {
        (string inCompany, Dictionary<string, object?> parameters) = IdInCompany(id, companyId);
        IReadOnlyList<SiteInfo> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {SiteColumns} FROM kernel.site WHERE id = @id{inCompany}", parameters, ReadSite, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public static Task<IReadOnlyList<SiteInfo>> ListSitesAsync(IUnitOfWork unitOfWork, Guid companyId, int limit, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {SiteColumns} FROM kernel.site WHERE company_id = @company ORDER BY code LIMIT @limit", new Dictionary<string, object?> { ["company"] = companyId, ["limit"] = limit }, ReadSite, cancellationToken);

    public static async Task<bool> SiteCodeExistsAsync(IUnitOfWork unitOfWork, Guid companyId, Code code, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.site WHERE company_id = @company AND code = @code", new Dictionary<string, object?> { ["company"] = companyId, ["code"] = code.Value }, cancellationToken) is long count && count > 0;

    public static Task InsertSiteAsync(IUnitOfWork unitOfWork, SiteInfo site, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "INSERT INTO kernel.site (id, company_id, code, name, time_zone, created_at, created_by, updated_at, updated_by, version) VALUES (@id, @company, @code, @name, @timeZone, @now, @by, @now, @by, @version)",
            SiteParameters(site, now, by),
            cancellationToken);

    public static Task<int> UpdateSiteAsync(IUnitOfWork unitOfWork, SiteInfo site, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = SiteParameters(site, now, by);
        parameters["expected"] = expectedVersion;
        return unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.site SET name = @name, time_zone = @timeZone, updated_at = @now, updated_by = @by, version = @version WHERE id = @id AND version = @expected",
            parameters,
            cancellationToken);
    }

    /// <summary>Lock class 1 of a warehouse default change (Lock order, 1b): the site row first, so two default changes on one site run one after the other.</summary>
    public static Task LockSiteAsync(IUnitOfWork unitOfWork, Guid siteId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel, "SELECT id FROM kernel.site WHERE id = @id FOR UPDATE", Id(siteId), cancellationToken);

    private static Dictionary<string, object?> SiteParameters(SiteInfo site, DateTimeOffset now, UserId by) => new()
    {
        ["id"] = site.Id,
        ["company"] = site.CompanyId,
        ["code"] = site.Code.Value,
        ["name"] = site.Name,
        ["timeZone"] = site.TimeZone,
        ["now"] = now,
        ["by"] = by.Value,
        ["version"] = site.Version,
    };

    private static SiteInfo ReadSite(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), Code.From(reader.GetString(2)), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt64(5));

    // ---- warehouse ---------------------------------------------------------------------------------------------------

    public static async Task<WarehouseView?> FindWarehouseAsync(IUnitOfWork unitOfWork, Guid id, Guid? companyId, CancellationToken cancellationToken)
    {
        (string inCompany, Dictionary<string, object?> parameters) = IdInCompany(id, companyId);
        IReadOnlyList<WarehouseView> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {WarehouseColumns} FROM kernel.warehouse WHERE id = @id{inCompany}", parameters, ReadWarehouse, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public static Task<IReadOnlyList<WarehouseView>> ListWarehousesAsync(IUnitOfWork unitOfWork, Guid companyId, Guid? siteId, int limit, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = new() { ["company"] = companyId, ["limit"] = limit };
        string ofSite = string.Empty;
        if (siteId is { } site)
        {
            parameters["site"] = site;
            ofSite = " AND site_id = @site";
        }

        return unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {WarehouseColumns} FROM kernel.warehouse WHERE company_id = @company{ofSite} ORDER BY code LIMIT @limit", parameters, ReadWarehouse, cancellationToken);
    }

    public static async Task<bool> WarehouseCodeExistsAsync(IUnitOfWork unitOfWork, Guid companyId, Code code, CancellationToken cancellationToken) =>
        await unitOfWork.ScalarAsync(DatabaseRole.Kernel, "SELECT count(*) FROM kernel.warehouse WHERE company_id = @company AND code = @code", new Dictionary<string, object?> { ["company"] = companyId, ["code"] = code.Value }, cancellationToken) is long count && count > 0;

    /// <summary>Lock class 1 of a default change, after the site row: the site's warehouses in id order, locked in one statement (R-LED-08).</summary>
    public static Task<IReadOnlyList<WarehouseView>> LockSiteWarehousesAsync(IUnitOfWork unitOfWork, Guid siteId, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel, $"SELECT {WarehouseColumns} FROM kernel.warehouse WHERE site_id = @id ORDER BY id FOR UPDATE", Id(siteId), ReadWarehouse, cancellationToken);

    public static Task InsertWarehouseAsync(IUnitOfWork unitOfWork, WarehouseView warehouse, DateTimeOffset now, UserId by, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by, version) VALUES (@id, @site, @company, @code, @name, @isDefault, @status, @now, @by, @now, @by, @version)",
            WarehouseParameters(warehouse, now, by),
            cancellationToken);

    /// <summary>The versioned update of a warehouse: name and default flag (the site, the code and, until step 7, the status never change here).</summary>
    public static Task<int> UpdateWarehouseAsync(IUnitOfWork unitOfWork, WarehouseView warehouse, long expectedVersion, DateTimeOffset now, UserId by, CancellationToken cancellationToken)
    {
        Dictionary<string, object?> parameters = WarehouseParameters(warehouse, now, by);
        parameters["expected"] = expectedVersion;
        return unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
            "UPDATE kernel.warehouse SET name = @name, is_default = @isDefault, updated_at = @now, updated_by = @by, version = @version WHERE id = @id AND version = @expected",
            parameters,
            cancellationToken);
    }

    private static Dictionary<string, object?> WarehouseParameters(WarehouseView warehouse, DateTimeOffset now, UserId by) => new()
    {
        ["id"] = warehouse.Id,
        ["site"] = warehouse.SiteId,
        ["company"] = warehouse.CompanyId,
        ["code"] = warehouse.Code.Value,
        ["name"] = warehouse.Name,
        ["isDefault"] = warehouse.IsDefault,
        ["status"] = warehouse.Status.ToString(),
        ["now"] = now,
        ["by"] = by.Value,
        ["version"] = warehouse.Version,
    };

    private static WarehouseView ReadWarehouse(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), Code.From(reader.GetString(3)), reader.GetString(4), reader.GetBoolean(5), Enum.Parse<MasterDataStatus>(reader.GetString(6)), reader.GetInt64(7));
}

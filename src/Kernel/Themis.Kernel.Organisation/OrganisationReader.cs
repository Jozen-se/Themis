using System.Data.Common;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// Reads the organisation tables on the caller's unit of work as themis_kernel (R-DB-01), converts UTC instants to
/// business dates in the company's time zone (R-TIME-01) and resolves the fiscal period of a business date, which
/// steps 7 and 8 take the entry's period from and check (R-LED-04).
/// </summary>
internal sealed class OrganisationReader : IOrganisationReader
{
    internal const string CompanyColumns = "id, code, party_id, legal_name, organisation_number, vat_number, country_code, base_currency, time_zone, fiscal_year_start_month, chart_of_accounts_id, rounding_mode, tax_rounding_level, status, version";

    // RR-62: every read hands the connection back as themis_app on successful return.
    public Task<CompanyInfo?> GetCompanyAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken) =>
        GetCompanyCoreAsync(unitOfWork, companyId, cancellationToken).ThenResetRoleAsync(unitOfWork, cancellationToken);

    public Task<WarehouseInfo?> GetWarehouseAsync(IUnitOfWork unitOfWork, Guid warehouseId, CancellationToken cancellationToken) =>
        GetWarehouseCoreAsync(unitOfWork, warehouseId, cancellationToken).ThenResetRoleAsync(unitOfWork, cancellationToken);

    public Task<Result<FiscalPeriodInfo>> ResolveFiscalPeriodAsync(IUnitOfWork unitOfWork, Guid companyId, BusinessDate businessDate, CancellationToken cancellationToken) =>
        ResolveFiscalPeriodCoreAsync(unitOfWork, companyId, businessDate, cancellationToken).ThenResetRoleAsync(unitOfWork, cancellationToken);

    private static async Task<CompanyInfo?> GetCompanyCoreAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        IReadOnlyList<CompanyInfo> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"SELECT {CompanyColumns} FROM kernel.company WHERE id = @id",
            new Dictionary<string, object?> { ["id"] = companyId },
            ReadCompany,
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    private static async Task<WarehouseInfo?> GetWarehouseCoreAsync(IUnitOfWork unitOfWork, Guid warehouseId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        IReadOnlyList<WarehouseInfo> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"""
            SELECT w.id, w.code, w.name, w.is_default, w.status,
                   s.id, s.company_id, s.code, s.name, s.time_zone, s.version,
                   {string.Join(", ", CompanyColumns.Split(", ").Select(column => "c." + column))}
            FROM kernel.warehouse w
            JOIN kernel.site s ON s.id = w.site_id AND s.company_id = w.company_id
            JOIN kernel.company c ON c.id = s.company_id
            WHERE w.id = @id
            """,
            new Dictionary<string, object?> { ["id"] = warehouseId },
            reader => new WarehouseInfo(
                reader.GetGuid(0),
                Code.From(reader.GetString(1)),
                reader.GetString(2),
                reader.GetBoolean(3),
                Enum.Parse<MasterDataStatus>(reader.GetString(4)),
                new SiteInfo(reader.GetGuid(5), reader.GetGuid(6), Code.From(reader.GetString(7)), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt64(10)),
                ReadCompany(reader, offset: 11)),
            cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public BusinessDate BusinessDateFor(CompanyInfo company, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(company);
        // R-TIME-01: system time is UTC; the business date is the calendar date in the company's time zone.
        return BusinessDate.InZone(instant, company.TimeZone);
    }

    private static async Task<Result<FiscalPeriodInfo>> ResolveFiscalPeriodCoreAsync(IUnitOfWork unitOfWork, Guid companyId, BusinessDate businessDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        IReadOnlyList<FiscalPeriodInfo> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            "SELECT id, fiscal_year_id, period_number, start_date, end_date, status FROM kernel.fiscal_period WHERE company_id = @company AND @date BETWEEN start_date AND end_date",
            new Dictionary<string, object?> { ["company"] = companyId, ["date"] = businessDate.Value },
            reader => new FiscalPeriodInfo(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                new BusinessDate(reader.GetFieldValue<DateOnly>(3)),
                new BusinessDate(reader.GetFieldValue<DateOnly>(4)),
                Enum.Parse<FiscalPeriodStatus>(reader.GetString(5))),
            cancellationToken);

        // R-LED-04: a date outside every fiscal year has no period to post into.
        if (rows.Count == 0)
        {
            return Error.Rule("R-LED-04", "fiscal_period_not_found", $"No fiscal period of the company covers {businessDate}.");
        }

        // RR-44: the exclusion constraint ex_fiscal_period_no_overlap keeps a company's periods disjoint, so exactly one row covers a date; more is a defect, never a guess.
        if (rows.Count > 1)
        {
            throw new InvalidOperationException($"{rows.Count} fiscal periods of company {companyId} cover {businessDate}; periods must not overlap (RR-44).");
        }

        return rows[0];
    }

    internal static CompanyInfo ReadCompany(DbDataReader reader) => ReadCompany(reader, offset: 0);

    /// <summary>Reads <see cref="CompanyColumns"/> from the given offset; the commands and the reads share it.</summary>
    internal static CompanyInfo ReadCompany(DbDataReader reader, int offset) => new(
        reader.GetGuid(offset),
        Code.From(reader.GetString(offset + 1)),
        reader.GetGuid(offset + 2),
        reader.GetString(offset + 3),
        reader.GetString(offset + 4),
        reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5),
        CountryCode.From(reader.GetString(offset + 6)),
        CurrencyCode.From(reader.GetString(offset + 7)),
        reader.GetString(offset + 8),
        reader.GetInt32(offset + 9),
        reader.GetGuid(offset + 10),
        Enum.Parse<RoundingMode>(reader.GetString(offset + 11)),
        Enum.Parse<TaxRoundingLevel>(reader.GetString(offset + 12)),
        Enum.Parse<CompanyStatus>(reader.GetString(offset + 13)),
        reader.GetInt64(offset + 14));
}

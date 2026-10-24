using System.Globalization;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>The rejection codes of the organisation commands (milestone 2 step 3), one place so the tests and the pull request can list them.</summary>
internal static class OrganisationErrors
{
    public const string RuleUnique = "R-MD-05";
    public const string RuleFixed = "R-MD-06";
    public const string RuleGuard = "R-DOC-01";

    // R-MD-05 (422): a business key that already exists within its uniqueness scope.
    public const string CompanyCodeDuplicate = "company_code_duplicate";
    public const string OrganisationNumberDuplicate = "organisation_number_duplicate";
    public const string ChartCodeDuplicate = "chart_of_accounts_code_duplicate";
    public const string SiteCodeDuplicate = "site_code_duplicate";
    public const string WarehouseCodeDuplicate = "warehouse_code_duplicate";

    // R-MD-06 (422): an attribute the Specification fixes, after its trigger (D-65).
    public const string CompanyCodeFixed = "company_code_fixed";
    public const string CompanyBaseCurrencyFixed = "company_base_currency_fixed";
    public const string CompanyTimeZoneFixed = "company_time_zone_fixed";
    public const string CompanyCountryCodeFixed = "company_country_code_fixed";
    public const string CompanyOrganisationNumberFixed = "company_organisation_number_fixed";
    public const string CompanyRoundingModeFixed = "company_rounding_mode_fixed";
    public const string CompanyChartOfAccountsFixed = "company_chart_of_accounts_fixed";
    public const string CompanyFiscalYearStartMonthFixed = "company_fiscal_year_start_month_fixed";
    public const string ChartCodeFixed = "chart_of_accounts_code_fixed";
    public const string SiteCodeFixed = "site_code_fixed";
    public const string WarehouseCodeFixed = "warehouse_code_fixed";

    // R-DOC-01 (422): a guard without a rule of its own (Specification, State machines).
    public const string WarehouseNotActive = "warehouse_not_active";
    public const string WarehouseDefaultTaken = "warehouse_default_taken";

    // 404 and 412.
    public const string CompanyNotFound = "company_not_found";
    public const string ChartNotFound = "chart_of_accounts_not_found";
    public const string SiteNotFound = "site_not_found";
    public const string WarehouseNotFound = "warehouse_not_found";
    public const string VersionStale = "version_stale";

    // 400 found inside the transaction, stored as the key's result (Build Brief, Errors).
    public const string BaseCurrencyUnknown = "base_currency_unknown";
    public const string ChartUnknown = "chart_of_accounts_unknown";
    public const string CompanyUnknown = "company_unknown";
    public const string SiteUnknown = "site_unknown";
    public const string SiteOtherCompany = "site_other_company";
}

/// <summary>Shape checks the validators share (RR-51): pure, before the transaction, never stored.</summary>
internal static class OrganisationValidation
{
    public const int NameMaxLength = 200;
    public const int NumberMaxLength = 50;
    public const int TimeZoneMaxLength = 64;

    public static Error? RequiredCode(Code code, string field, string what) =>
        code.Value is null ? Error.Validation($"{field}_required", $"{what} needs a code.", details: Field(field)) : null;

    public static Error? RequiredId(Guid id, string field, string what) =>
        id == Guid.Empty ? Error.Validation($"{field}_required", $"{what} is required.", details: Field(field)) : null;

    public static IEnumerable<Error> Name(string? value, string field, string what, bool required = true, int maxLength = NameMaxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                yield return Error.Validation($"{field}_required", $"{what} is required.", details: Field(field));
            }
        }
        else if (value.Trim().Length > maxLength)
        {
            yield return Error.Validation($"{field}_too_long", $"{what} holds at most {maxLength} characters.", details: Field(field));
        }
    }

    /// <summary>
    /// A time zone is an IANA name the API converts with (BusinessDate.InZone, R-TIME-01); a Windows id, which .NET would
    /// also resolve, is refused, and so is any id the runtime does not know.
    /// </summary>
    public static IEnumerable<Error> TimeZone(string? value, string field, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                yield return Error.Validation($"{field}_required", "A time zone (an IANA name such as Europe/Stockholm) is required.", details: Field(field));
            }

            yield break;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > TimeZoneMaxLength || !TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out TimeZoneInfo? zone) || !zone.HasIanaId)
        {
            yield return Error.Validation($"{field}_invalid", $"'{trimmed}' is not an IANA time zone name.", details: Field(field));
        }
    }

    public static Error? Month(int month) =>
        month is < 1 or > 12 ? Error.Validation("fiscal_year_start_month_out_of_range", "The fiscal year start month is 1 to 12.", details: Field("fiscalYearStartMonth")) : null;

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string> Field(string field) => new(StringComparer.Ordinal) { ["field"] = field };
}

/// <summary>
/// The changed fields of the audit events (R-AUD-01): every field from nothing to its value on create, the fields
/// whose value differs on update, the version always. Values are text, decimals and dates included (D-37).
/// </summary>
internal static class OrganisationAudit
{
    public const string CompanyEntity = "Company";
    public const string PartyEntity = "Party";
    public const string ChartEntity = "ChartOfAccounts";
    public const string SiteEntity = "Site";
    public const string WarehouseEntity = "Warehouse";

    public const string CompanyType = "COMPANY";
    public const string PartyType = "PARTY";
    public const string ChartType = "CHART_OF_ACCOUNTS";
    public const string SiteType = "SITE";
    public const string WarehouseType = "WAREHOUSE";

    public static IReadOnlyDictionary<string, FieldChange> CompanyChanges(CompanyInfo? before, CompanyInfo after) => Diff(before is null ? null : Fields(before), Fields(after));

    public static IReadOnlyDictionary<string, FieldChange> PartyChanges(PartyRow? before, PartyRow after) => Diff(before is null ? null : Fields(before), Fields(after));

    public static IReadOnlyDictionary<string, FieldChange> ChartChanges(ChartOfAccountsView? before, ChartOfAccountsView after) => Diff(before is null ? null : Fields(before), Fields(after));

    public static IReadOnlyDictionary<string, FieldChange> SiteChanges(SiteInfo? before, SiteInfo after) => Diff(before is null ? null : Fields(before), Fields(after));

    public static IReadOnlyDictionary<string, FieldChange> WarehouseChanges(WarehouseView? before, WarehouseView after) => Diff(before is null ? null : Fields(before), Fields(after));

    private static Dictionary<string, string?> Fields(CompanyInfo c) => new(StringComparer.Ordinal)
    {
        ["code"] = c.Code.Value,
        ["partyId"] = c.PartyId.ToString(),
        ["legalName"] = c.LegalName,
        ["organisationNumber"] = c.OrganisationNumber,
        ["vatNumber"] = c.VatNumber,
        ["countryCode"] = c.CountryCode.Value,
        ["baseCurrency"] = c.BaseCurrency.Value,
        ["timeZone"] = c.TimeZone,
        ["fiscalYearStartMonth"] = c.FiscalYearStartMonth.ToString(CultureInfo.InvariantCulture),
        ["chartOfAccountsId"] = c.ChartOfAccountsId.ToString(),
        ["roundingMode"] = c.RoundingMode.ToString(),
        ["taxRoundingLevel"] = c.TaxRoundingLevel.ToString(),
        ["status"] = c.Status.ToString(),
        ["version"] = c.Version.ToString(CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, string?> Fields(PartyRow p) => new(StringComparer.Ordinal)
    {
        ["partyNumber"] = p.PartyNumber,
        ["partyType"] = p.PartyType.ToString(),
        ["name"] = p.Name,
        ["organisationNumber"] = p.OrganisationNumber,
        ["vatNumber"] = p.VatNumber,
        ["countryCode"] = p.CountryCode.Value,
        ["language"] = p.Language,
        ["status"] = p.Status.ToString(),
        ["duplicateKey"] = p.DuplicateKey,
        ["version"] = p.Version.ToString(CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, string?> Fields(ChartOfAccountsView c) => new(StringComparer.Ordinal)
    {
        ["code"] = c.Code.Value,
        ["name"] = c.Name,
        ["template"] = c.Template,
        ["version"] = c.Version.ToString(CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, string?> Fields(SiteInfo s) => new(StringComparer.Ordinal)
    {
        ["companyId"] = s.CompanyId.ToString(),
        ["code"] = s.Code.Value,
        ["name"] = s.Name,
        ["timeZone"] = s.TimeZone,
        ["version"] = s.Version.ToString(CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, string?> Fields(WarehouseView w) => new(StringComparer.Ordinal)
    {
        ["siteId"] = w.SiteId.ToString(),
        ["companyId"] = w.CompanyId.ToString(),
        ["code"] = w.Code.Value,
        ["name"] = w.Name,
        ["isDefault"] = w.IsDefault ? "True" : "False",
        ["status"] = w.Status.ToString(),
        ["version"] = w.Version.ToString(CultureInfo.InvariantCulture),
    };

    private static Dictionary<string, FieldChange> Diff(Dictionary<string, string?>? before, Dictionary<string, string?> after)
    {
        Dictionary<string, FieldChange> changes = new(StringComparer.Ordinal);
        foreach ((string field, string? value) in after)
        {
            string? previous = before is null ? null : before[field];
            if (before is null || !string.Equals(previous, value, StringComparison.Ordinal))
            {
                changes[field] = new FieldChange(previous, value);
            }
        }

        return changes;
    }
}

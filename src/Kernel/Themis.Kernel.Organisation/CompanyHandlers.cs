using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// COMPANY.CREATE (milestone 2 step 3 brief, item 3; D-64, D-65): the company and, in the same command, its own party.
/// Checks inside the transaction, stored as the key's result (Build Brief, Errors): the base currency is a seeded
/// Currency and the chart exists (400); the code is unique per installation and the organisation number per
/// country, compared normalised by the duplicate key's function (R-MD-05, 422; the unique indexes ux_company_code and
/// ux_company_country_organisation_number catch a race, RR-45). The rounding mode defaults to the
/// company.rounding_mode setting definition, never a literal (P-11). The party is an Organisation party from the
/// legal name, organisation number, VAT number and country, in the installation's default locale, numbered from the
/// installation-level PARTY series as the last lock class (R-LED-08); no duplicate check against other parties runs
/// before step 8. One audit event per row and the events PARTY_CREATED (no company: an installation-scope aggregate)
/// and COMPANY_CREATED (the new company's id) (D-44, D-50).
/// </summary>
internal sealed class CreateCompanyHandler : ICommandHandler<CreateCompany, CompanyInfo>
{
    private readonly IOrganisationCreateSeam _seam;
    private readonly ISettingsReader _settings;
    private readonly INumberSeriesService _numbers;

    public CreateCompanyHandler(IOrganisationCreateSeam seam, ISettingsReader settings, INumberSeriesService numbers)
    {
        _seam = seam;
        _settings = settings;
        _numbers = numbers;
    }

    public async Task<Result<CompanyInfo>> HandleAsync(CreateCompany command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        if (!await OrganisationStore.CurrencyExistsAsync(unitOfWork, command.BaseCurrency, cancellationToken))
        {
            return Error.Validation(OrganisationErrors.BaseCurrencyUnknown, $"Currency {command.BaseCurrency} is not a currency.", details: Field("baseCurrency"));
        }

        if (await OrganisationStore.FindChartAsync(unitOfWork, command.ChartOfAccountsId, cancellationToken) is null)
        {
            return Error.Validation(OrganisationErrors.ChartUnknown, $"Chart of accounts {command.ChartOfAccountsId} does not exist.", details: Field("chartOfAccountsId"));
        }

        // R-MD-05: the company code is unique per installation (Specification, Company).
        if (await OrganisationStore.CompanyCodeExistsAsync(unitOfWork, command.Code, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.CompanyCodeDuplicate, $"Company code {command.Code} already exists.");
        }

        // R-MD-05, D-64: two companies never share an organisation number in one country, compared normalised like the duplicate key (R-MD-01).
        string legalName = command.LegalName.Trim();
        string organisationNumber = command.OrganisationNumber.Trim();
        string? vatNumber = OrganisationValidation.Trimmed(command.VatNumber);
        string normalised = DuplicateKeys.Normalise(organisationNumber);
        if (await OrganisationStore.OrganisationNumberTakenAsync(unitOfWork, command.CountryCode, normalised, exceptCompanyId: null, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.OrganisationNumberDuplicate, $"A company of {command.CountryCode} with organisation number {organisationNumber} already exists.");
        }

        // P-11, D-58: the documented default of company.rounding_mode comes from the setting definition, read like any setting.
        RoundingMode roundingMode = command.RoundingMode ?? await DefaultRoundingModeAsync(unitOfWork, cancellationToken);
        string? language = await OrganisationStore.InstallationLocaleAsync(unitOfWork, cancellationToken);

        await _seam.AfterDuplicateChecksAsync(OrganisationAudit.CompanyEntity, command.Code, cancellationToken);

        // Last lock class (R-LED-08): the PARTY number, taken after every check; only writes follow (D-61).
        Result<IReadOnlyList<NumberTaken>> numbers = await _numbers.TakeInstallationAsync(unitOfWork, [new NumberRequest(DocumentTypes.Party)], context.Now, context.Principal.UserId, cancellationToken);
        if (numbers.IsFailure)
        {
            return Result<CompanyInfo>.Failure(numbers.Errors);
        }

        PartyRow party = new(EntityId.New(), numbers.Value[0].Number, PartyType.Organisation, legalName, organisationNumber, vatNumber, command.CountryCode, language, MasterDataStatus.Active,
            DuplicateKeys.ForParty(organisationNumber, vatNumber, legalName, postalCode: null), Version: 1);
        CompanyInfo company = new(EntityId.New(), command.Code, party.Id, legalName, organisationNumber, vatNumber, command.CountryCode, command.BaseCurrency, command.TimeZone.Trim(),
            command.FiscalYearStartMonth, command.ChartOfAccountsId, roundingMode, command.TaxRoundingLevel!.Value, CompanyStatus.Active, Version: 1);

        await OrganisationStore.InsertPartyAsync(unitOfWork, party, context.Now, context.Principal.UserId, cancellationToken);
        await OrganisationStore.InsertCompanyAsync(unitOfWork, company, normalised, context.Now, context.Principal.UserId, cancellationToken);

        // R-AUD-01, D-44, D-50: one audit event and one event per created row; the party is an installation-scope aggregate.
        context.Audit.Record(OrganisationAudit.PartyEntity, party.Id, OrganisationAudit.PartyChanges(null, party));
        context.Audit.Record(OrganisationAudit.CompanyEntity, company.Id, OrganisationAudit.CompanyChanges(null, company));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.PartyType, "CREATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.PartyType, party.Id, companyId: null, party);
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.CompanyType, "CREATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.CompanyType, company.Id, company.Id, company);
        return company;
    }

    private async Task<RoundingMode> DefaultRoundingModeAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        // Without a company target a Column-backed definition answers its documented default (Specification, Resolution).
        SettingResolution resolution = await _settings.ResolveAsync(unitOfWork, SettingKeys.CompanyRoundingMode, SettingTarget.Installation, cancellationToken);
        return resolution.Value is { } value && Enum.TryParse(value, out RoundingMode mode)
            ? mode
            : throw new InvalidOperationException($"Setting {SettingKeys.CompanyRoundingMode} has no documented default to apply (P-11).");
    }

    private static Dictionary<string, string> Field(string field) => new(StringComparer.Ordinal) { ["field"] = field };
}

/// <summary>
/// COMPANY.UPDATE (step 3 brief, item 4; D-65, R-MD-06): the changeable attributes on the current version. The
/// versioned update of the company row is lock class 1, which waits behind a posting's FOR SHARE (class 1b); the
/// R-MD-06 trigger checks run after it in their own statements, so they see a posting that committed meanwhile, and a
/// blocked change is 422 with a code naming the attribute. The code never changes. The company's own party follows
/// the legal name, organisation number, VAT number and country in the same command, with its duplicate key
/// recomputed, its audit event and PARTY_UPDATED only when one of them changed (D-64). The VAT rounding level is
/// fixed only from the first posted tax line, which milestone 2 never writes, so no check exists for it yet (D-65).
/// </summary>
internal sealed class UpdateCompanyHandler : ICommandHandler<UpdateCompany, CompanyInfo>
{
    private readonly ICompanyUpdateSeam _seam;

    public UpdateCompanyHandler(ICompanyUpdateSeam seam)
    {
        _seam = seam;
    }

    public async Task<Result<CompanyInfo>> HandleAsync(UpdateCompany command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        CompanyInfo? before = await OrganisationStore.FindCompanyAsync(unitOfWork, command.Id, cancellationToken);
        if (before is null)
        {
            return Error.NotFound(OrganisationErrors.CompanyNotFound, $"Company {command.Id} does not exist.");
        }

        if (before.Version != command.ExpectedVersion)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Company {command.Id} is at version {before.Version}, not {command.ExpectedVersion}.");
        }

        // R-MD-06: the code is fixed from creation.
        if (command.Code is { } code && code != before.Code)
        {
            return Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.CompanyCodeFixed, $"The code of company {before.Code} never changes (R-MD-06).");
        }

        if (!await OrganisationStore.CurrencyExistsAsync(unitOfWork, command.BaseCurrency, cancellationToken))
        {
            return Error.Validation(OrganisationErrors.BaseCurrencyUnknown, $"Currency {command.BaseCurrency} is not a currency.", details: Field("baseCurrency"));
        }

        if (await OrganisationStore.FindChartAsync(unitOfWork, command.ChartOfAccountsId, cancellationToken) is null)
        {
            return Error.Validation(OrganisationErrors.ChartUnknown, $"Chart of accounts {command.ChartOfAccountsId} does not exist.", details: Field("chartOfAccountsId"));
        }

        string legalName = command.LegalName.Trim();
        string organisationNumber = command.OrganisationNumber.Trim();
        string? vatNumber = OrganisationValidation.Trimmed(command.VatNumber);
        string normalised = DuplicateKeys.Normalise(organisationNumber);
        CompanyInfo after = before with
        {
            LegalName = legalName,
            OrganisationNumber = organisationNumber,
            VatNumber = vatNumber,
            CountryCode = command.CountryCode,
            BaseCurrency = command.BaseCurrency,
            TimeZone = command.TimeZone.Trim(),
            FiscalYearStartMonth = command.FiscalYearStartMonth,
            ChartOfAccountsId = command.ChartOfAccountsId,
            RoundingMode = command.RoundingMode!.Value,
            TaxRoundingLevel = command.TaxRoundingLevel!.Value,
            Version = before.Version + 1,
        };

        // R-MD-05, D-64: checked again on change, against every other company of the country.
        bool identityChanged = after.CountryCode != before.CountryCode || !string.Equals(normalised, DuplicateKeys.Normalise(before.OrganisationNumber), StringComparison.Ordinal);
        if (identityChanged && await OrganisationStore.OrganisationNumberTakenAsync(unitOfWork, after.CountryCode, normalised, before.Id, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.OrganisationNumberDuplicate, $"A company of {after.CountryCode} with organisation number {organisationNumber} already exists.");
        }

        // Lock class 1 (R-LED-08): the versioned update waits behind a posting's FOR SHARE of class 1b (D-65).
        if (await OrganisationStore.UpdateCompanyAsync(unitOfWork, after, normalised, command.ExpectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Company {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        await _seam.AfterVersionedUpdateAsync(before.Id, cancellationToken);

        // R-MD-06 (D-65): the triggers, read in their own statements after the update returned, so they see a posting that
        // committed while the update waited; a NOT EXISTS inside the UPDATE would run on the snapshot from before the wait.
        List<Error> fixedAttributes = await FixedAttributeViolationsAsync(unitOfWork, before, after, cancellationToken);
        if (fixedAttributes.Count > 0)
        {
            return Result<CompanyInfo>.Failure(fixedAttributes);
        }

        // D-64: the company's own party follows the four fields it shares, in the same command, with its own audit event and event.
        if (!string.Equals(before.LegalName, after.LegalName, StringComparison.Ordinal) || !string.Equals(before.OrganisationNumber, after.OrganisationNumber, StringComparison.Ordinal)
            || !string.Equals(before.VatNumber, after.VatNumber, StringComparison.Ordinal) || before.CountryCode != after.CountryCode)
        {
            PartyRow partyBefore = await OrganisationStore.FindPartyAsync(unitOfWork, before.PartyId, cancellationToken)
                ?? throw new InvalidOperationException($"Company {before.Id} names party {before.PartyId}, which does not exist (D-64).");
            PartyRow partyAfter = partyBefore with
            {
                Name = legalName,
                OrganisationNumber = organisationNumber,
                VatNumber = vatNumber,
                CountryCode = after.CountryCode,
                DuplicateKey = DuplicateKeys.ForParty(organisationNumber, vatNumber, legalName, postalCode: null),
                Version = partyBefore.Version + 1,
            };
            // Lock class 1 continues: the company row, then its party row (Lock order, 1b). Nothing else writes a company's party before step 8.
            if (await OrganisationStore.UpdatePartyAsync(unitOfWork, partyAfter, partyBefore.Version, context.Now, context.Principal.UserId, cancellationToken) != 1)
            {
                throw new InvalidOperationException($"Party {partyBefore.Id} changed under COMPANY.UPDATE; no other command writes a company's own party before step 8 (D-64).");
            }

            context.Audit.Record(OrganisationAudit.PartyEntity, partyAfter.Id, OrganisationAudit.PartyChanges(partyBefore, partyAfter));
            context.Outbox.Publish(EventNames.Generated(OrganisationAudit.PartyType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.PartyType, partyAfter.Id, companyId: null, partyAfter);
        }

        context.Audit.Record(OrganisationAudit.CompanyEntity, after.Id, OrganisationAudit.CompanyChanges(before, after));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.CompanyType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.CompanyType, after.Id, after.Id, after);
        return after;
    }

    /// <summary>The R-MD-06 checks of the Specification's Fixed attributes table, one error per blocked attribute (D-65).</summary>
    private static async Task<List<Error>> FixedAttributeViolationsAsync(IUnitOfWork unitOfWork, CompanyInfo before, CompanyInfo after, CancellationToken cancellationToken)
    {
        List<(string Code, string Attribute, bool Changed)> postingFixed =
        [
            (OrganisationErrors.CompanyBaseCurrencyFixed, "base currency", before.BaseCurrency != after.BaseCurrency),
            (OrganisationErrors.CompanyTimeZoneFixed, "time zone", !string.Equals(before.TimeZone, after.TimeZone, StringComparison.Ordinal)),
            (OrganisationErrors.CompanyCountryCodeFixed, "country", before.CountryCode != after.CountryCode),
            (OrganisationErrors.CompanyOrganisationNumberFixed, "organisation number", !string.Equals(before.OrganisationNumber, after.OrganisationNumber, StringComparison.Ordinal)),
            (OrganisationErrors.CompanyRoundingModeFixed, "rounding mode", before.RoundingMode != after.RoundingMode),
        ];
        bool chartChanged = before.ChartOfAccountsId != after.ChartOfAccountsId;
        bool monthChanged = before.FiscalYearStartMonth != after.FiscalYearStartMonth;

        List<Error> errors = [];
        bool? hasPostings = null;
        if (postingFixed.Any(attribute => attribute.Changed) || chartChanged)
        {
            hasPostings = await OrganisationStore.CompanyHasPostingsAsync(unitOfWork, before.Id, cancellationToken);
        }

        foreach ((string code, string attribute, bool changed) in postingFixed)
        {
            if (changed && hasPostings == true)
            {
                errors.Add(Error.Rule(OrganisationErrors.RuleFixed, code, $"The {attribute} of company {before.Code} is fixed since its first posting (R-MD-06, D-65)."));
            }
        }

        if (chartChanged && (hasPostings == true || await OrganisationStore.CompanyHasPostingProfileRulesAsync(unitOfWork, before.Id, cancellationToken)))
        {
            errors.Add(Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.CompanyChartOfAccountsFixed, $"The chart of accounts of company {before.Code} is fixed since its first posting profile rule or posting (R-MD-06, D-65)."));
        }

        if (monthChanged && await OrganisationStore.CompanyHasFiscalYearsAsync(unitOfWork, before.Id, cancellationToken))
        {
            errors.Add(Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.CompanyFiscalYearStartMonthFixed, $"The fiscal year start month of company {before.Code} is fixed since its first fiscal year (R-MD-06, D-65)."));
        }

        return errors;
    }

    private static Dictionary<string, string> Field(string field) => new(StringComparer.Ordinal) { ["field"] = field };
}

/// <summary>The scope of COMPANY.UPDATE: the company it names, once loaded (Build Brief, Authorisation); a missing company is 404.</summary>
internal sealed class UpdateCompanyScopeResolver : IScopeResolver<UpdateCompany>
{
    public async Task<Result<RequestScope>> ResolveAsync(UpdateCompany request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CompanyInfo? company = await OrganisationStore.FindCompanyAsync(unitOfWork, request.Id, cancellationToken);
        return company is null ? Error.NotFound(OrganisationErrors.CompanyNotFound, $"Company {request.Id} does not exist.") : RequestScope.ForCompany(company.Id);
    }
}

/// <summary>Shape checks of COMPANY.CREATE before the transaction (RR-51); a value that breaks a rule is the handler's 422.</summary>
internal sealed class CreateCompanyValidator : ICommandValidator<CreateCompany>
{
    public IEnumerable<Error> Validate(CreateCompany command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OrganisationValidation.RequiredCode(command.Code, "code", "A company") is { } code)
        {
            yield return code;
        }

        foreach (Error error in CompanyValidation.Shared(command.LegalName, command.OrganisationNumber, command.VatNumber, command.CountryCode, command.BaseCurrency, command.TimeZone, command.FiscalYearStartMonth, command.ChartOfAccountsId, command.TaxRoundingLevel))
        {
            yield return error;
        }
    }
}

internal sealed class UpdateCompanyValidator : ICommandValidator<UpdateCompany>
{
    public IEnumerable<Error> Validate(UpdateCompany command)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (Error error in CompanyValidation.Shared(command.LegalName, command.OrganisationNumber, command.VatNumber, command.CountryCode, command.BaseCurrency, command.TimeZone, command.FiscalYearStartMonth, command.ChartOfAccountsId, command.TaxRoundingLevel))
        {
            yield return error;
        }

        if (command.RoundingMode is null)
        {
            yield return Error.Validation("rounding_mode_required", "The update names the rounding mode.", details: new Dictionary<string, string>(StringComparer.Ordinal) { ["field"] = "roundingMode" });
        }
    }
}

internal static class CompanyValidation
{
    public static IEnumerable<Error> Shared(string? legalName, string? organisationNumber, string? vatNumber, CountryCode country, CurrencyCode currency, string? timeZone, int month, Guid chartId, TaxRoundingLevel? taxRoundingLevel)
    {
        foreach (Error error in OrganisationValidation.Name(legalName, "legal_name", "The legal name")
            .Concat(OrganisationValidation.Name(organisationNumber, "organisation_number", "The organisation number", maxLength: OrganisationValidation.NumberMaxLength))
            .Concat(OrganisationValidation.Name(vatNumber, "vat_number", "The VAT number", required: false, maxLength: OrganisationValidation.NumberMaxLength))
            .Concat(OrganisationValidation.TimeZone(timeZone, "time_zone", required: true)))
        {
            yield return error;
        }

        if (country.Value is null)
        {
            yield return Error.Validation("country_code_required", "A country code is required.", details: Field("countryCode"));
        }

        if (currency.Value is null)
        {
            yield return Error.Validation("base_currency_required", "A base currency is required.", details: Field("baseCurrency"));
        }

        if (OrganisationValidation.Month(month) is { } monthError)
        {
            yield return monthError;
        }

        if (OrganisationValidation.RequiredId(chartId, "chart_of_accounts", "A chart of accounts") is { } chart)
        {
            yield return chart;
        }

        // D-65: the VAT rounding level is stated explicitly while D-10 is open, since there is no default to apply.
        if (taxRoundingLevel is null)
        {
            yield return Error.Validation("tax_rounding_level_required", "The VAT rounding level (Line or Document) is required while D-10 is open.", details: Field("taxRoundingLevel"));
        }
    }

    private static Dictionary<string, string> Field(string field) => new(StringComparer.Ordinal) { ["field"] = field };
}

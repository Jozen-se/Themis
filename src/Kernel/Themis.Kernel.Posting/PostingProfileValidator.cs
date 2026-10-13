using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// The posting profile rule check of step 6, used by the fixture seeder now and by the Phase 0 commands later: the
/// account belongs to the company's chart, the account is Active (Specification, PostingProfileRule), control roles
/// map only to control accounts and other roles only to non-control accounts (R-PP-04), and a rule sets only the
/// mapping keys of its role (R-PP-06). The database constraints enforce the same and are the second line of defence.
/// </summary>
internal static class PostingProfileValidator
{
    public static Result Validate(PostingProfileRuleDefinition rule, AccountDefinition account, Guid companyChartId)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(account);
        List<Error> errors = [];

        AccountRoleDefinition? role = AccountRoles.Find(rule.AccountRole);
        if (role is null)
        {
            errors.Add(Error.Validation("account_role_unknown", $"{rule.AccountRole} is not an account role."));
        }

        if (account.Id != rule.AccountId)
        {
            throw new ArgumentException("The account is not the rule's account.", nameof(account));
        }

        if (account.ChartId != companyChartId)
        {
            errors.Add(Error.Validation("account_other_chart", $"Account {account.AccountNumber} belongs to another chart than the company's."));
        }

        if (account.Status != MasterDataStatus.Active)
        {
            errors.Add(Error.Validation("account_not_active", $"Account {account.AccountNumber} is {account.Status}; a rule's account must be Active."));
        }

        if (role is not null)
        {
            // R-PP-04: control roles map only to control accounts and other roles only to non-control accounts.
            if (role.ExpectsControlAccount != account.IsControl)
            {
                errors.Add(Error.Rule("R-PP-04", "account_control_mismatch", role.ExpectsControlAccount
                    ? $"{role.Code} maps only to a control account; {account.AccountNumber} is not one."
                    : $"{role.Code} maps only to a non-control account; {account.AccountNumber} is a control account."));
            }

            // R-PP-06: a rule sets only the mapping keys of its role.
            foreach (MappingKeys key in AccountRoles.RankingOrder)
            {
                if (rule.Keys.Has(key) && !role.Uses(key))
                {
                    errors.Add(Error.Rule("R-PP-06", "key_not_in_role", $"{role.Code} does not use the {key} key; a rule for it leaves that key empty."));
                }
            }
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }

    /// <summary>Checks every rule of a fixture against the accounts it defines, with the companies' charts supplied by the caller.</summary>
    public static Result CheckFixture(GeneralLedgerFixtureDefinition fixture, IReadOnlyDictionary<Guid, Guid> chartByCompany)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(chartByCompany);
        List<Error> errors = [];
        Dictionary<Guid, AccountDefinition> accounts = fixture.Accounts.ToDictionary(account => account.Id);

        foreach (PostingProfileRuleDefinition rule in fixture.Rules)
        {
            if (!accounts.TryGetValue(rule.AccountId, out AccountDefinition? account))
            {
                errors.Add(Error.Validation("account_unknown", $"Rule {rule.Id} names account {rule.AccountId}, which the fixture does not define."));
                continue;
            }

            if (!chartByCompany.TryGetValue(rule.CompanyId, out Guid chartId))
            {
                errors.Add(Error.Validation("company_unknown", $"Rule {rule.Id} names company {rule.CompanyId}, which has no chart."));
                continue;
            }

            Result result = Validate(rule, account, chartId);
            if (result.IsFailure)
            {
                errors.AddRange(result.Errors);
            }
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

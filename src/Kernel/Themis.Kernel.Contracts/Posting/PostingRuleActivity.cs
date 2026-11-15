using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Posting;

/// <summary>
/// A posting profile rule that counts as active under R-PP-05 (D-56, D-69), with what the rejection names: the rule's
/// company, role, keys and validity (Specification, Account commands).
/// </summary>
public sealed record ActivePostingRule(Guid RuleId, Guid CompanyId, Code CompanyCode, string AccountRole, PostingKeys Keys, BusinessDate ValidFrom, BusinessDate? ValidTo)
{
    /// <summary>The rule as text for the rejection message: "company SE01, role INVENTORY, item group TRADE_GOODS, valid from 2026-01-01 (open-ended)".</summary>
    public string Describe() =>
        $"company {CompanyCode.Value}, role {AccountRole}, {Keys.Describe()}, valid from {ValidFrom}" + (ValidTo is { } to ? $" to {to} (exclusive)" : " (open-ended)");
}

/// <summary>
/// The one definition of an active posting profile rule (D-69): a rule is inactive only when its whole validity lies
/// inside Closed fiscal periods of its company; every other rule is active, so in a company without fiscal years every
/// rule is active, and so is a rule whose validity lies wholly before the company's first fiscal year. The check runs
/// by account over the rules of every company, not by chart. ACCOUNT.BLOCK and ACCOUNT.DEACTIVATE use it (R-PP-05);
/// VALIDATE_POSTING_PROFILE (step 6) uses the same definition. Implemented by the posting capability.
/// </summary>
public interface IPostingRuleActivity
{
    /// <summary>The active rules of every company that reference the account, ordered by company code, role and valid_from.</summary>
    Task<IReadOnlyList<ActivePostingRule>> ActiveRulesForAccountAsync(IUnitOfWork unitOfWork, Guid accountId, CancellationToken cancellationToken);
}

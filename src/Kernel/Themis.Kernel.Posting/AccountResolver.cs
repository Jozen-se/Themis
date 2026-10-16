using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// The resolution algorithm of the Phase 0 Specification (Posting profile: the resolution algorithm) as a pure
/// function over the candidate rules: valid on the posting date, every non-empty key of the rule equal to the
/// line's value, only the role's mapping keys compared (R-PP-06); ranked by item posting group, then party posting
/// group or ship-to scope, then tax code, then reason code, a rule that sets an earlier key always winning (R-PP-01,
/// D-19); the top candidate is unique because two rules with the same keys cannot be valid at the same time
/// (R-MD-02). No candidate is R-PP-02; a winner whose account is not Active is R-PP-02 too, and resolution never
/// falls back to a less specific rule (D-49).
/// </summary>
internal static class AccountResolver
{
    public const string RuleMissing = "posting_rule_missing";
    public const string AccountNotActive = "posting_rule_account_not_active";

    public static Result<AccountResolution> Resolve(string accountRole, BusinessDate postingDate, PostingKeys keys, IEnumerable<PostingRuleCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(candidates);
        AccountRoleDefinition role = AccountRoles.Get(accountRole);

        PostingRuleCandidate? winner = null;
        foreach (PostingRuleCandidate candidate in candidates)
        {
            if (!string.Equals(candidate.AccountRole, role.Code, StringComparison.Ordinal) || !candidate.IsValidOn(postingDate) || !Matches(role, candidate.Keys, keys))
            {
                continue;
            }

            if (winner is null || Compare(role, candidate.Keys, winner.Keys) > 0)
            {
                winner = candidate;
            }
        }

        if (winner is null)
        {
            return Error.Rule("R-PP-02", RuleMissing, $"no account for {role.Code}, {keys.Describe()}, posting date {postingDate}");
        }

        if (winner.AccountStatus != MasterDataStatus.Active)
        {
            return Error.Rule("R-PP-02", AccountNotActive, $"no active account for {role.Code}, {keys.Describe()}, posting date {postingDate}: rule {winner.Id} maps to account {winner.AccountId}, which is {winner.AccountStatus}");
        }

        return new AccountResolution(winner.AccountId, role.Code, winner.Id);
    }

    /// <summary>Step 1: every non-empty key of the rule equals the line's value; only the role's keys are compared (R-PP-06).</summary>
    private static bool Matches(AccountRoleDefinition role, PostingKeys rule, PostingKeys line)
    {
        foreach (MappingKeys key in AccountRoles.RankingOrder)
        {
            if (!role.Uses(key) || !rule.Has(key))
            {
                continue;
            }

            if (!Equal(key, rule, line))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Step 2: in the fixed key order, a rule that sets a key beats one that leaves it empty, whatever the later keys hold (D-19).</summary>
    private static int Compare(AccountRoleDefinition role, PostingKeys left, PostingKeys right)
    {
        foreach (MappingKeys key in AccountRoles.RankingOrder)
        {
            if (!role.Uses(key))
            {
                continue;
            }

            bool l = left.Has(key);
            bool r = right.Has(key);
            if (l != r)
            {
                return l ? 1 : -1;
            }
        }

        return 0;
    }

    private static bool Equal(MappingKeys key, PostingKeys rule, PostingKeys line) => key switch
    {
        MappingKeys.ItemPostingGroup => rule.ItemPostingGroup == line.ItemPostingGroup,
        MappingKeys.PartyPostingGroup => rule.PartyPostingGroup == line.PartyPostingGroup,
        MappingKeys.ShipToScope => rule.ShipToScope == line.ShipToScope,
        MappingKeys.TaxCode => rule.TaxCodeId == line.TaxCodeId,
        MappingKeys.ReasonCode => rule.ReasonCode == line.ReasonCode,
        _ => false,
    };
}

using System.Data.Common;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// The active-rule definition of D-69, written once (step 4 brief, item 2): a posting profile rule is inactive only
/// when its whole validity, valid_from inclusive to valid_to exclusive, lies inside the Closed fiscal periods of its
/// company; an open-ended rule is never inside them, and a company without Closed periods has no inactive rule. The
/// Closed periods are aggregated into one date multirange per company, so adjacent Closed periods cover a validity that
/// spans them and a gap between fiscal years does not. ACCOUNT.BLOCK and ACCOUNT.DEACTIVATE read it by account over
/// the rules of every company (R-PP-05); VALIDATE_POSTING_PROFILE (step 6) uses <see cref="ActivePredicate"/>.
/// </summary>
internal sealed class PostingRuleActivity : IPostingRuleActivity
{
    /// <summary>
    /// The SQL predicate "the rule aliased <paramref name="rule"/> is active" (D-69). Fiscal period dates are inclusive,
    /// rule validity ends exclusive (Specification, Effective dating).
    /// </summary>
    public static string ActivePredicate(string rule) =>
        $$"""
        NOT ({{rule}}.valid_to IS NOT NULL AND daterange({{rule}}.valid_from, {{rule}}.valid_to, '[)') <@ COALESCE(
            (SELECT range_agg(daterange(p.start_date, p.end_date, '[]')) FROM kernel.fiscal_period p WHERE p.company_id = {{rule}}.company_id AND p.status = 'Closed'),
            '{}'::datemultirange))
        """;

    // RR-62: hands the connection back as themis_app on successful return.
    public Task<IReadOnlyList<ActivePostingRule>> ActiveRulesForAccountAsync(IUnitOfWork unitOfWork, Guid accountId, CancellationToken cancellationToken) =>
        ActiveRulesCoreAsync(unitOfWork, accountId, cancellationToken).ThenResetRoleAsync(unitOfWork, cancellationToken);

    private static Task<IReadOnlyList<ActivePostingRule>> ActiveRulesCoreAsync(IUnitOfWork unitOfWork, Guid accountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        // R-PP-05, D-69: by account over the rules of every company, not by chart (Gate B ties a rule's account to its company's chart).
        return unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"""
            SELECT r.id, r.company_id, c.code, r.account_role, r.item_posting_group, r.party_posting_group, r.ship_to_scope, r.tax_code_id, r.reason_code, r.valid_from, r.valid_to
            FROM kernel.posting_profile_rule r
            JOIN kernel.company c ON c.id = r.company_id
            WHERE r.account_id = @account AND {ActivePredicate("r")}
            ORDER BY c.code, r.account_role, r.valid_from, r.id
            """,
            new Dictionary<string, object?> { ["account"] = accountId },
            Read,
            cancellationToken);
    }

    private static ActivePostingRule Read(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        Code.From(reader.GetString(2)),
        reader.GetString(3),
        new PostingKeys(
            reader.IsDBNull(4) ? null : Code.From(reader.GetString(4)),
            reader.IsDBNull(5) ? null : Enum.Parse<PartyPostingGroup>(reader.GetString(5)),
            reader.IsDBNull(6) ? null : Enum.Parse<ShipToScope>(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetGuid(7),
            reader.IsDBNull(8) ? null : Enum.Parse<ReasonCode>(reader.GetString(8))),
        new BusinessDate(reader.GetFieldValue<DateOnly>(9)),
        reader.IsDBNull(10) ? null : new BusinessDate(reader.GetFieldValue<DateOnly>(10)));
}

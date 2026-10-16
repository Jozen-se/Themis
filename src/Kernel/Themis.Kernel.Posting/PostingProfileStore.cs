using System.Data.Common;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>
/// Raw SQL on the command's unit of work as themis_kernel (R-DB-01): the posting profile rules of a company and role
/// with their account's status, for the resolver. The kernel data context stays in Themis.Kernel.Persistence; this
/// capability reaches the tables through the unit of work only (ARCH-08).
/// </summary>
internal static class PostingProfileStore
{
    public static Task<IReadOnlyList<PostingRuleCandidate>> RulesAsync(IUnitOfWork unitOfWork, Guid companyId, string accountRole, CancellationToken cancellationToken) =>
        unitOfWork.QueryAsync(DatabaseRole.Kernel,
            """
            SELECT r.id, r.account_role, r.item_posting_group, r.party_posting_group, r.ship_to_scope, r.tax_code_id, r.reason_code, r.account_id, a.status, r.valid_from, r.valid_to
            FROM kernel.posting_profile_rule r
            JOIN kernel.account a ON a.id = r.account_id
            WHERE r.company_id = @company AND r.account_role = @role
            """,
            new Dictionary<string, object?> { ["company"] = companyId, ["role"] = accountRole },
            Read,
            cancellationToken);

    private static PostingRuleCandidate Read(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        new PostingKeys(
            reader.IsDBNull(2) ? null : Code.From(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Enum.Parse<PartyPostingGroup>(reader.GetString(3)),
            reader.IsDBNull(4) ? null : Enum.Parse<ShipToScope>(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : Enum.Parse<ReasonCode>(reader.GetString(6))),
        reader.GetGuid(7),
        Enum.Parse<MasterDataStatus>(reader.GetString(8)),
        new BusinessDate(reader.GetFieldValue<DateOnly>(9)),
        reader.IsDBNull(10) ? null : new BusinessDate(reader.GetFieldValue<DateOnly>(10)));
}

/// <summary>The resolver on a unit of work: loads the company's rules for the role and applies <see cref="AccountResolver"/>.</summary>
internal sealed class PostingProfileAccountResolver : IAccountResolver
{
    public async Task<Result<AccountResolution>> ResolveAsync(IUnitOfWork unitOfWork, Guid companyId, string accountRole, BusinessDate postingDate, PostingKeys keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        IReadOnlyList<PostingRuleCandidate> candidates = await PostingProfileStore.RulesAsync(unitOfWork, companyId, accountRole, cancellationToken);
        return AccountResolver.Resolve(accountRole, postingDate, keys, candidates);
    }
}

using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Posting;

/// <summary>Phase 0 Specification, Account: a row of a chart of accounts, as the test and development fixture defines it.</summary>
public sealed record AccountDefinition(
    Guid Id,
    Guid ChartId,
    Code AccountNumber,
    string Name,
    AccountType AccountType,
    bool IsControl,
    MasterDataStatus Status);

/// <summary>
/// Phase 0 Specification, PostingProfileRule: a company's mapping of an account role to an account, keyed by the
/// role's mapping keys (empty means any value), valid from <paramref name="ValidFrom"/> inclusive to
/// <paramref name="ValidTo"/> exclusive (empty means open-ended).
/// </summary>
public sealed record PostingProfileRuleDefinition(
    Guid Id,
    Guid CompanyId,
    string AccountRole,
    Guid AccountId,
    BusinessDate ValidFrom,
    BusinessDate? ValidTo = null,
    Code? ItemPostingGroup = null,
    PartyPostingGroup? PartyPostingGroup = null,
    ShipToScope? ShipToScope = null,
    Guid? TaxCodeId = null,
    ReasonCode? ReasonCode = null)
{
    public PostingKeys Keys => new(ItemPostingGroup, PartyPostingGroup, ShipToScope, TaxCodeId, ReasonCode);
}

/// <summary>The accounts and posting profile rules of a test and development fixture, written by the migrator's seeder (step 6 brief).</summary>
public sealed record GeneralLedgerFixtureDefinition(
    IReadOnlyList<AccountDefinition> Accounts,
    IReadOnlyList<PostingProfileRuleDefinition> Rules);

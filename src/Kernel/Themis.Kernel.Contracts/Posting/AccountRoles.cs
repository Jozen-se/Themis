using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Posting;

/// <summary>The mapping keys a posting profile rule may set, one flag per key of the Specification's posting groups table.</summary>
[Flags]
public enum MappingKeys
{
    None = 0,
    ItemPostingGroup = 1,
    PartyPostingGroup = 2,
    ShipToScope = 4,
    TaxCode = 8,
    ReasonCode = 16,
}

/// <summary>Phase 0 Specification, Account: the five types of the chart.</summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
}

/// <summary>Phase 0 Specification, Posting groups: the fixed party posting group values, a key of RECEIVABLES.</summary>
public enum PartyPostingGroup
{
    Domestic,
    EU,
    NonEU,
}

/// <summary>Phase 0 Specification, Posting groups: the ship-to scope derived per line, a key of COGS and REVENUE.</summary>
public enum ShipToScope
{
    Domestic,
    EU,
    NonEU,
}

/// <summary>Phase 0 Specification, Posting groups: the inventory adjustment reason codes, a key of INVENTORY_ADJUSTMENT.</summary>
public enum ReasonCode
{
    OpeningBalance,
    CountDifference,
    Scrap,
    Other,
}

/// <summary>
/// One account role of the Specification's system-defined list: its code, the mapping keys its rules may set (R-PP-06)
/// and whether it must map to a control account (R-PP-04).
/// </summary>
public sealed record AccountRoleDefinition(string Code, MappingKeys Keys, bool ExpectsControlAccount)
{
    public bool Uses(MappingKeys key) => (Keys & key) == key && key != MappingKeys.None;
}

/// <summary>
/// The account roles of Phases 0 to 2 (Phase 0 Specification, Posting profile: account roles and posting groups):
/// system-defined, so a catalogue in code and no table (D-46). The persistence capability generates the database
/// constraints for the role list, R-PP-06 and R-PP-04 from this catalogue, and an integration test proves the
/// database agrees with it, so a role added here without its migration fails CI.
/// </summary>
public static class AccountRoles
{
    public const string Inventory = "INVENTORY";
    public const string CostOfGoodsSold = "COGS";
    public const string Revenue = "REVENUE";
    public const string Receivables = "RECEIVABLES";
    public const string OutputVat = "OUTPUT_VAT";
    public const string Bank = "BANK";
    public const string PaymentDifference = "PAYMENT_DIFFERENCE";
    public const string RealisedFx = "REALISED_FX";
    public const string Rounding = "ROUNDING";
    public const string InventoryAdjustment = "INVENTORY_ADJUSTMENT";
    public const string VatSettlement = "VAT_SETTLEMENT";
    public const string OpeningBalance = "OPENING_BALANCE";

    /// <summary>The catalogue, in the order of the Specification's table.</summary>
    public static IReadOnlyList<AccountRoleDefinition> All { get; } =
    [
        new(Inventory, MappingKeys.ItemPostingGroup, ExpectsControlAccount: true),
        new(CostOfGoodsSold, MappingKeys.ItemPostingGroup | MappingKeys.ShipToScope, ExpectsControlAccount: false),
        new(Revenue, MappingKeys.ItemPostingGroup | MappingKeys.ShipToScope | MappingKeys.TaxCode, ExpectsControlAccount: false),
        new(Receivables, MappingKeys.PartyPostingGroup, ExpectsControlAccount: true),
        new(OutputVat, MappingKeys.TaxCode, ExpectsControlAccount: true),
        new(Bank, MappingKeys.None, ExpectsControlAccount: false),
        new(PaymentDifference, MappingKeys.None, ExpectsControlAccount: false),
        new(RealisedFx, MappingKeys.None, ExpectsControlAccount: false),
        new(Rounding, MappingKeys.None, ExpectsControlAccount: false),
        new(InventoryAdjustment, MappingKeys.ItemPostingGroup | MappingKeys.ReasonCode, ExpectsControlAccount: false),
        new(VatSettlement, MappingKeys.None, ExpectsControlAccount: false),
        new(OpeningBalance, MappingKeys.None, ExpectsControlAccount: false),
    ];

    /// <summary>The keys in the Specification's ranking order (R-PP-01, D-19): party posting group and ship-to scope share the second rank, each role using at most one of them.</summary>
    public static IReadOnlyList<MappingKeys> RankingOrder { get; } =
        [MappingKeys.ItemPostingGroup, MappingKeys.PartyPostingGroup, MappingKeys.ShipToScope, MappingKeys.TaxCode, MappingKeys.ReasonCode];

    public static AccountRoleDefinition? Find(string code) => All.FirstOrDefault(role => string.Equals(role.Code, code, StringComparison.Ordinal));

    public static AccountRoleDefinition Get(string code) => Find(code) ?? throw new ArgumentException($"{code} is not an account role.", nameof(code));

    public static bool Contains(string code) => Find(code) is not null;
}

/// <summary>
/// The values of a journal line's mapping keys, as the posting service hands them to the resolver: only the keys of
/// the role are read (R-PP-06); the rest stay empty.
/// </summary>
public sealed record PostingKeys(
    Code? ItemPostingGroup = null,
    PartyPostingGroup? PartyPostingGroup = null,
    ShipToScope? ShipToScope = null,
    Guid? TaxCodeId = null,
    ReasonCode? ReasonCode = null)
{
    public static PostingKeys None { get; } = new();

    public bool Has(MappingKeys key) => key switch
    {
        MappingKeys.ItemPostingGroup => ItemPostingGroup is not null,
        MappingKeys.PartyPostingGroup => PartyPostingGroup is not null,
        MappingKeys.ShipToScope => ShipToScope is not null,
        MappingKeys.TaxCode => TaxCodeId is not null,
        MappingKeys.ReasonCode => ReasonCode is not null,
        _ => false,
    };

    /// <summary>The set keys as text, for the R-PP-02 message: "item group TRADE_GOODS, reason code Scrap".</summary>
    public string Describe()
    {
        List<string> parts = [];
        if (ItemPostingGroup is { } group)
        {
            parts.Add($"item group {group.Value}");
        }

        if (PartyPostingGroup is { } party)
        {
            parts.Add($"party group {party}");
        }

        if (ShipToScope is { } scope)
        {
            parts.Add($"ship-to scope {scope}");
        }

        if (TaxCodeId is { } tax)
        {
            parts.Add($"tax code {tax}");
        }

        if (ReasonCode is { } reason)
        {
            parts.Add($"reason code {reason}");
        }

        return parts.Count == 0 ? "no keys" : string.Join(", ", parts);
    }
}

/// <summary>A posting profile rule as the resolver sees it: its keys, its account with the account's status, and its validity.</summary>
public sealed record PostingRuleCandidate(
    Guid Id,
    string AccountRole,
    PostingKeys Keys,
    Guid AccountId,
    MasterDataStatus AccountStatus,
    BusinessDate ValidFrom,
    BusinessDate? ValidTo)
{
    /// <summary>valid_from inclusive, valid_to exclusive (Specification, Effective dating).</summary>
    public bool IsValidOn(BusinessDate date) => date >= ValidFrom && (ValidTo is null || date < ValidTo.Value);
}

/// <summary>What the resolver returns for a journal line: the account, the role and the rule, for explainability (resolution step 5, P-05).</summary>
public sealed record AccountResolution(Guid AccountId, string AccountRole, Guid PostingRuleId);

/// <summary>
/// Resolves the account for a role on a company's posting profile (R-PP-01, R-PP-02, D-19, D-49): reads the
/// candidate rules on the command's unit of work and applies the Specification's algorithm.
/// </summary>
public interface IAccountResolver
{
    Task<Result<AccountResolution>> ResolveAsync(IUnitOfWork unitOfWork, Guid companyId, string accountRole, BusinessDate postingDate, PostingKeys keys, CancellationToken cancellationToken);
}

using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Organisation;

/// <summary>
/// ACCOUNT.CREATE (milestone 2 step 4 brief, item 1; D-69): an account in a chart. The number is a Code, unique per
/// chart (R-MD-05), and with the chart fixed from creation (R-MD-06); the type and the control flag are fixed from the
/// account's first journal line or first posting profile rule in any company. Status Active at create. Installation
/// scope and Sensitive, because a chart may serve several companies (D-69, RR-52).
/// </summary>
[CommandRules("D-69", "R-MD-05", "RR-45")]
[CommandAudits("Account")]
[CommandEvents("ACCOUNT_CREATED")]
[CommandErrors("chart_required", "account_number_required", "name_required", "name_too_long", "account_type_required", "chart_of_accounts_unknown", "account_number_duplicate")]
[Command("ACCOUNT.CREATE")]
public sealed record CreateAccount(Guid ChartId, Code AccountNumber, string Name, AccountType? AccountType, bool IsControl = false) : ICommand<AccountView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>
/// ACCOUNT.UPDATE (step 4 brief, items 1 and 3; D-69, R-MD-06): name, type and control flag on the current version
/// (R-API-02), in Active or Blocked. The command takes no chart: a chart or number in the body that differs from the
/// stored one is rejected under R-MD-06 (account_chart_fixed, account_number_fixed), an equal one ignored. A type or
/// control flag change after the account's first journal line or first posting profile rule, in any company, is
/// rejected under R-MD-06 with the attribute's code.
/// </summary>
[CommandRules("D-69", "R-API-02", "R-MD-06", "R-PP-04")]
[CommandAudits("Account")]
[CommandEvents("ACCOUNT_UPDATED")]
[CommandErrors("name_required", "name_too_long", "account_type_required", "account_not_found", "version_stale", "transition_not_allowed", "account_number_fixed", "account_chart_fixed", "account_type_fixed", "account_control_fixed")]
[Command("ACCOUNT.UPDATE")]
public sealed record UpdateAccount(Guid Id, string Name, AccountType? AccountType, bool IsControl, long ExpectedVersion, Code? AccountNumber = null, Guid? ChartId = null) : ICommand<AccountView>, IVersionedCommand;

/// <summary>
/// ACCOUNT.BLOCK (step 4 brief, item 2): Active to Blocked on the current version, with a reason of at most 500
/// characters that goes into the audit event's changes and the ACCOUNT_BLOCKED payload, with no column of its own
/// (D-69). Rejected under R-PP-05 while an active posting profile rule of any company references the account (D-56).
/// </summary>
[CommandRules("D-56", "D-69", "R-API-02", "R-PP-05")]
[CommandAudits("Account")]
[CommandEvents("ACCOUNT_BLOCKED")]
[CommandErrors("reason_required", "reason_too_long", "account_not_found", "version_stale", "transition_not_allowed", "account_used_by_posting_rule")]
[Command("ACCOUNT.BLOCK")]
public sealed record BlockAccount(Guid Id, string? Reason, long ExpectedVersion) : ICommand<AccountView>, IVersionedCommand;

/// <summary>ACCOUNT.UNBLOCK (step 4 brief, item 2): Blocked to Active on the current version; no guard.</summary>
[CommandRules("D-69", "R-API-02")]
[CommandAudits("Account")]
[CommandEvents("ACCOUNT_UNBLOCKED")]
[CommandErrors("account_not_found", "version_stale", "transition_not_allowed")]
[Command("ACCOUNT.UNBLOCK")]
public sealed record UnblockAccount(Guid Id, long ExpectedVersion) : ICommand<AccountView>, IVersionedCommand;

/// <summary>
/// ACCOUNT.DEACTIVATE (step 4 brief, item 2): Active or Blocked to Inactive on the current version. Rejected under
/// R-PP-05 while an active posting profile rule references the account (D-56), and under R-DOC-01 while its lifetime
/// balance is not zero in any company (D-69).
/// </summary>
[CommandRules("D-56", "D-69", "R-API-02", "R-DOC-01", "R-PP-05")]
[CommandAudits("Account")]
[CommandEvents("ACCOUNT_DEACTIVATED")]
[CommandErrors("account_not_found", "version_stale", "transition_not_allowed", "account_used_by_posting_rule", "account_balance_not_zero")]
[Command("ACCOUNT.DEACTIVATE")]
public sealed record DeactivateAccount(Guid Id, long ExpectedVersion) : ICommand<AccountView>, IVersionedCommand;

/// <summary>ACCOUNT.REACTIVATE (step 4 brief, item 2): Inactive to Active on the current version; no guard.</summary>
[CommandRules("D-69", "R-API-02")]
[CommandAudits("Account")]
[CommandEvents("ACCOUNT_REACTIVATED")]
[CommandErrors("account_not_found", "version_stale", "transition_not_allowed")]
[Command("ACCOUNT.REACTIVATE")]
public sealed record ReactivateAccount(Guid Id, long ExpectedVersion) : ICommand<AccountView>, IVersionedCommand;

/// <summary>An account as the commands and queries return it: its chart, number, name, type, control flag, status and version (R-API-02).</summary>
public sealed record AccountView(Guid Id, Guid ChartId, Code AccountNumber, string Name, AccountType AccountType, bool IsControl, MasterDataStatus Status, long Version) : IVersionedResult;

/// <summary>GET /v1/kernel/accounts/{id} (ACCOUNT.READ, D-47): installation scope (RR-52); a missing account is 404.</summary>
[Query("ACCOUNT.READ")]
public sealed record GetAccount(Guid Id) : IQuery<AccountView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>
/// GET /v1/kernel/charts-of-accounts/{chartId}/accounts (ACCOUNT.READ): the chart's accounts, ordered by account
/// number, at most <see cref="OrganisationRoutes.ListLimit"/> rows (the step 3 list limit); a missing chart is 404.
/// </summary>
[Query("ACCOUNT.READ")]
public sealed record ListAccounts(Guid ChartId) : IQuery<IReadOnlyList<AccountView>>, IScopedRequest
{
    public RequestScope Scope => RequestScope.Installation;
}

/// <summary>The account routes (step 4 brief, item 1; Build Brief, API conventions).</summary>
public static class AccountRoutes
{
    public const string Accounts = "/v1/kernel/accounts";
    public const string Account = "/v1/kernel/accounts/{id}";
    public const string AccountUpdate = "/v1/kernel/accounts/{id}/update";
    public const string AccountBlock = "/v1/kernel/accounts/{id}/block";
    public const string AccountUnblock = "/v1/kernel/accounts/{id}/unblock";
    public const string AccountDeactivate = "/v1/kernel/accounts/{id}/deactivate";
    public const string AccountReactivate = "/v1/kernel/accounts/{id}/reactivate";
    public const string ChartAccounts = "/v1/kernel/charts-of-accounts/{chartId}/accounts";

    /// <summary>The command routes the composition root maps through the kernel pipeline.</summary>
    public static IReadOnlyList<ICommandRoute> Commands { get; } =
    [
        new CommandRoute<CreateAccount, AccountView>(Accounts),
        new CommandRoute<UpdateAccount, AccountView>(AccountUpdate),
        new CommandRoute<BlockAccount, AccountView>(AccountBlock),
        new CommandRoute<UnblockAccount, AccountView>(AccountUnblock),
        new CommandRoute<DeactivateAccount, AccountView>(AccountDeactivate),
        new CommandRoute<ReactivateAccount, AccountView>(AccountReactivate),
    ];
}

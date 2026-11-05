using System.Globalization;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>The ACCOUNT_BLOCKED payload: the account with the reason, which has no column of its own (D-69).</summary>
internal sealed record AccountBlockedPayload(Guid Id, Guid ChartId, Code AccountNumber, string Name, AccountType AccountType, bool IsControl, MasterDataStatus Status, long Version, string Reason);

/// <summary>
/// ACCOUNT.CREATE (milestone 2 step 4 brief, item 1; D-69): an Active account in an existing chart. The chart is
/// checked inside the transaction and a missing one is 400 stored as the key's result (Build Brief, Errors); the
/// number is unique per chart (R-MD-05, 422; ux_account_chart_number catches a race, RR-45). One audit event and one
/// ACCOUNT_CREATED event with an empty company, since an account is an installation-scope aggregate (D-44, D-50).
/// </summary>
internal sealed class CreateAccountHandler : ICommandHandler<CreateAccount, AccountView>
{
    private readonly IOrganisationCreateSeam _seam;

    public CreateAccountHandler(IOrganisationCreateSeam seam)
    {
        _seam = seam;
    }

    public async Task<Result<AccountView>> HandleAsync(CreateAccount command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        if (await OrganisationStore.FindChartAsync(unitOfWork, command.ChartId, cancellationToken) is null)
        {
            return Error.Validation(OrganisationErrors.ChartUnknown, $"Chart of accounts {command.ChartId} does not exist.", details: AccountValidation.Field("chartId"));
        }

        // R-MD-05: the account number is unique per chart (Specification, Account).
        if (await AccountStore.NumberExistsAsync(unitOfWork, command.ChartId, command.AccountNumber, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.AccountNumberDuplicate, $"Account {command.AccountNumber} already exists in chart of accounts {command.ChartId}.");
        }

        await _seam.AfterDuplicateChecksAsync(OrganisationAudit.AccountEntity, command.AccountNumber, cancellationToken);

        AccountView account = new(EntityId.New(), command.ChartId, command.AccountNumber, command.Name.Trim(), command.AccountType!.Value, command.IsControl, MasterDataStatus.Active, Version: 1);
        await AccountStore.InsertAsync(unitOfWork, account, context.Now, context.Principal.UserId, cancellationToken);
        context.Audit.Record(OrganisationAudit.AccountEntity, account.Id, OrganisationAudit.AccountChanges(null, account));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.AccountType, "CREATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.AccountType, account.Id, companyId: null, account);
        return account;
    }
}

/// <summary>
/// ACCOUNT.UPDATE (step 4 brief, items 1 and 3; D-69, R-MD-06): name, type and control flag on the current version,
/// in Active or Blocked. The number and the chart are fixed from creation, so a differing one in the body is 422 and an
/// equal one ignored. As in COMPANY.UPDATE, the versioned update of the row comes first (lock class 1, which waits
/// behind a posting's FOR SHARE of the account), then the trigger check in its own statement, so it sees a journal
/// line or rule that committed meanwhile; a type or control change after the account's first journal line or first
/// posting profile rule in any company is 422 with the attribute's code. The type and control flag are written only
/// after that check, so fk_posting_profile_rule_account_control never answers in its place (R-PP-04).
/// </summary>
internal sealed class UpdateAccountHandler : ICommandHandler<UpdateAccount, AccountView>
{
    public async Task<Result<AccountView>> HandleAsync(UpdateAccount command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        Result<AccountView> loaded = await AccountLifecycle.LoadAsync(unitOfWork, command.Id, command.ExpectedVersion, "UPDATE", [MasterDataStatus.Active, MasterDataStatus.Blocked], cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded;
        }

        AccountView before = loaded.Value;

        // R-MD-06 (D-69): the number and the chart are fixed from creation.
        if (command.AccountNumber is { } number && number != before.AccountNumber)
        {
            return Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.AccountNumberFixed, $"The number of account {before.AccountNumber} never changes (R-MD-06).");
        }

        if (command.ChartId is { } chart && chart != before.ChartId)
        {
            return Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.AccountChartFixed, $"Account {before.AccountNumber} stays in chart of accounts {before.ChartId}; the chart never changes (R-MD-06, D-69).");
        }

        AccountView after = before with { Name = command.Name.Trim(), AccountType = command.AccountType!.Value, IsControl = command.IsControl, Version = before.Version + 1 };

        // Lock class 1 (R-LED-08): the versioned update, which waits behind a posting's FOR SHARE of the account (D-69).
        if (await AccountStore.UpdateAsync(unitOfWork, before.Id, after.Name, before.Status, after.Version, command.ExpectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Account {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        // R-MD-06 (D-69): the trigger, read in its own statement after the update returned.
        bool typeChanged = after.AccountType != before.AccountType;
        bool controlChanged = after.IsControl != before.IsControl;
        if (typeChanged || controlChanged)
        {
            if (await AccountStore.HasJournalLinesOrRulesAsync(unitOfWork, before.Id, cancellationToken))
            {
                List<Error> errors = [];
                if (typeChanged)
                {
                    errors.Add(Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.AccountTypeFixed, $"The type of account {before.AccountNumber} is fixed since its first journal line or posting profile rule (R-MD-06, D-69)."));
                }

                if (controlChanged)
                {
                    errors.Add(Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.AccountControlFixed, $"Whether account {before.AccountNumber} is a control account is fixed since its first journal line or posting profile rule (R-MD-06, R-PP-04, D-69)."));
                }

                return Result<AccountView>.Failure(errors);
            }

            await AccountStore.SetFixedAttributesAsync(unitOfWork, before.Id, after.AccountType, after.IsControl, cancellationToken);
        }

        context.Audit.Record(OrganisationAudit.AccountEntity, after.Id, OrganisationAudit.AccountChanges(before, after));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.AccountType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.AccountType, after.Id, companyId: null, after);
        return after;
    }
}

/// <summary>
/// ACCOUNT.BLOCK (step 4 brief, item 2; D-56, D-69): Active to Blocked. The versioned update first (lock class 1), then
/// the R-PP-05 guard in its own statement, so no posting or rule can slip between check and commit; rejected with
/// account_used_by_posting_rule, naming each active rule's company, role, keys and validity. The reason goes into the
/// audit event's changes and the ACCOUNT_BLOCKED payload (D-69).
/// </summary>
internal sealed class BlockAccountHandler : ICommandHandler<BlockAccount, AccountView>
{
    private readonly IPostingRuleActivity _rules;

    public BlockAccountHandler(IPostingRuleActivity rules)
    {
        _rules = rules;
    }

    public async Task<Result<AccountView>> HandleAsync(BlockAccount command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        string reason = command.Reason!.Trim();
        return await AccountLifecycle.TransitionAsync(command.Id, command.ExpectedVersion, "BLOCK", [MasterDataStatus.Active], MasterDataStatus.Blocked, context,
            async (account, ct) => await AccountLifecycle.ActiveRuleGuardAsync(_rules, context.UnitOfWork, account, "blocked", ct) is { } error ? [error] : [],
            reason, cancellationToken);
    }
}

/// <summary>ACCOUNT.UNBLOCK (step 4 brief, item 2): Blocked to Active on the current version; no guard.</summary>
internal sealed class UnblockAccountHandler : ICommandHandler<UnblockAccount, AccountView>
{
    public Task<Result<AccountView>> HandleAsync(UnblockAccount command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        return AccountLifecycle.TransitionAsync(command.Id, command.ExpectedVersion, "UNBLOCK", [MasterDataStatus.Blocked], MasterDataStatus.Active, context, guards: null, reason: null, cancellationToken);
    }
}

/// <summary>
/// ACCOUNT.DEACTIVATE (step 4 brief, item 2; D-56, D-69): Active or Blocked to Inactive. The versioned update first,
/// then each guard in its own statement: the R-PP-05 active-rule guard and the R-DOC-01 balance guard (zero over all
/// history in every company, code account_balance_not_zero). For an account these replace the generic lifecycle
/// guards (open documents, stock, open items, reservations), which do not apply to accounts. Every failing guard is
/// reported.
/// </summary>
internal sealed class DeactivateAccountHandler : ICommandHandler<DeactivateAccount, AccountView>
{
    private readonly IPostingRuleActivity _rules;

    public DeactivateAccountHandler(IPostingRuleActivity rules)
    {
        _rules = rules;
    }

    public Task<Result<AccountView>> HandleAsync(DeactivateAccount command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        return AccountLifecycle.TransitionAsync(command.Id, command.ExpectedVersion, "DEACTIVATE", [MasterDataStatus.Active, MasterDataStatus.Blocked], MasterDataStatus.Inactive, context, GuardsAsync, reason: null, cancellationToken);

        async Task<IReadOnlyList<Error>> GuardsAsync(AccountView account, CancellationToken ct)
        {
            List<Error> errors = [];
            if (await AccountLifecycle.ActiveRuleGuardAsync(_rules, context.UnitOfWork, account, "deactivated", ct) is { } ruleError)
            {
                errors.Add(ruleError);
            }

            // D-69: zero over all history in every company; until CLOSE_YEAR a Revenue or Expense account with history stays (known limitation).
            IReadOnlyList<AccountCompanyBalance> balances = await AccountStore.NonZeroBalancesAsync(context.UnitOfWork, account.Id, ct);
            if (balances.Count > 0)
            {
                string named = string.Join("; ", balances.Select(balance => $"company {balance.CompanyCode.Value}: {balance.Balance.ToString(CultureInfo.InvariantCulture)}"));
                errors.Add(Error.Rule(OrganisationErrors.RuleGuard, OrganisationErrors.AccountBalanceNotZero, $"Account {account.AccountNumber} cannot be deactivated while its balance over all history is not zero ({named}) (R-DOC-01, D-69)."));
            }

            return errors;
        }
    }
}

/// <summary>ACCOUNT.REACTIVATE (step 4 brief, item 2): Inactive to Active on the current version; no guard.</summary>
internal sealed class ReactivateAccountHandler : ICommandHandler<ReactivateAccount, AccountView>
{
    public Task<Result<AccountView>> HandleAsync(ReactivateAccount command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        return AccountLifecycle.TransitionAsync(command.Id, command.ExpectedVersion, "REACTIVATE", [MasterDataStatus.Inactive], MasterDataStatus.Active, context, guards: null, reason: null, cancellationToken);
    }
}

/// <summary>
/// The shared order of the account lifecycle commands (step 4 brief, item 2): load, check the version and the state
/// (R-API-02, R-DOC-01), the versioned update of the account row (lock class 1), then every guard in its own statement,
/// then the audit event and the generated event (D-44, D-50).
/// </summary>
internal static class AccountLifecycle
{
    public static async Task<Result<AccountView>> LoadAsync(IUnitOfWork unitOfWork, Guid id, long expectedVersion, string verb, IReadOnlyList<MasterDataStatus> from, CancellationToken cancellationToken)
    {
        AccountView? account = await AccountStore.FindAsync(unitOfWork, id, cancellationToken);
        if (account is null)
        {
            return Error.NotFound(OrganisationErrors.AccountNotFound, $"Account {id} does not exist.");
        }

        if (account.Version != expectedVersion)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Account {id} is at version {account.Version}, not {expectedVersion}.");
        }

        // R-DOC-01: only the listed transitions of the master data lifecycle (Specification, State machines, part 1).
        if (!from.Contains(account.Status))
        {
            return Error.Rule(OrganisationErrors.RuleGuard, OrganisationErrors.TransitionNotAllowed, $"ACCOUNT.{verb} is not allowed on account {account.AccountNumber} in {account.Status}.");
        }

        return account;
    }

    public static async Task<Result<AccountView>> TransitionAsync(
        Guid id,
        long expectedVersion,
        string verb,
        IReadOnlyList<MasterDataStatus> from,
        MasterDataStatus to,
        ICommandContext context,
        Func<AccountView, CancellationToken, Task<IReadOnlyList<Error>>>? guards,
        string? reason,
        CancellationToken cancellationToken)
    {
        IUnitOfWork unitOfWork = context.UnitOfWork;
        Result<AccountView> loaded = await LoadAsync(unitOfWork, id, expectedVersion, verb, from, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded;
        }

        AccountView before = loaded.Value;
        AccountView after = before with { Status = to, Version = before.Version + 1 };

        // Lock class 1 (R-LED-08): the versioned update first, so no concurrent posting or rule slips between the guard and the commit.
        if (await AccountStore.UpdateAsync(unitOfWork, before.Id, before.Name, to, after.Version, expectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Account {id} changed since version {expectedVersion} was read.");
        }

        if (guards is not null)
        {
            IReadOnlyList<Error> errors = await guards(after, cancellationToken);
            if (errors.Count > 0)
            {
                return Result<AccountView>.Failure(errors);
            }
        }

        Dictionary<string, FieldChange> changes = new(OrganisationAudit.AccountChanges(before, after), StringComparer.Ordinal);
        string eventName = EventNames.Generated(OrganisationAudit.AccountType, verb);
        object payload = after;
        if (reason is not null)
        {
            // D-69: the reason has no column; it is recorded in the audit event's changes and in the ACCOUNT_BLOCKED payload.
            changes["reason"] = new FieldChange(null, reason);
            payload = new AccountBlockedPayload(after.Id, after.ChartId, after.AccountNumber, after.Name, after.AccountType, after.IsControl, after.Status, after.Version, reason);
        }

        context.Audit.Record(OrganisationAudit.AccountEntity, after.Id, changes);
        context.Outbox.Publish(eventName, EventNames.DefaultSchemaVersion, OrganisationAudit.AccountType, after.Id, companyId: null, payload);
        return after;
    }

    /// <summary>
    /// R-PP-05 (D-56, D-69): the active posting profile rules of every company that reference the account, by account
    /// and not by chart; one error naming each rule's company, role, keys and validity, or null when there is none.
    /// </summary>
    public static async Task<Error?> ActiveRuleGuardAsync(IPostingRuleActivity rules, IUnitOfWork unitOfWork, AccountView account, string action, CancellationToken cancellationToken)
    {
        IReadOnlyList<ActivePostingRule> active = await rules.ActiveRulesForAccountAsync(unitOfWork, account.Id, cancellationToken);
        if (active.Count == 0)
        {
            return null;
        }

        Dictionary<string, string> details = new(StringComparer.Ordinal) { ["ruleIds"] = string.Join(",", active.Select(rule => rule.RuleId)) };
        return Error.Rule(OrganisationErrors.RulePostingProfile, OrganisationErrors.AccountUsedByPostingRule,
            $"Account {account.AccountNumber} cannot be {action}: active posting profile rules reference it ({string.Join("; ", active.Select(rule => rule.Describe()))}) (R-PP-05, D-56).",
            details);
    }
}

/// <summary>The scope of a command on an existing account: installation scope once the account is loaded (Build Brief, Authorisation); a missing account is 404.</summary>
internal sealed class AccountScopeResolver :
    IScopeResolver<UpdateAccount>,
    IScopeResolver<BlockAccount>,
    IScopeResolver<UnblockAccount>,
    IScopeResolver<DeactivateAccount>,
    IScopeResolver<ReactivateAccount>
{
    public Task<Result<RequestScope>> ResolveAsync(UpdateAccount request, IUnitOfWork unitOfWork, CancellationToken cancellationToken) => ResolveAsync(NotNull(request).Id, unitOfWork, cancellationToken);

    public Task<Result<RequestScope>> ResolveAsync(BlockAccount request, IUnitOfWork unitOfWork, CancellationToken cancellationToken) => ResolveAsync(NotNull(request).Id, unitOfWork, cancellationToken);

    public Task<Result<RequestScope>> ResolveAsync(UnblockAccount request, IUnitOfWork unitOfWork, CancellationToken cancellationToken) => ResolveAsync(NotNull(request).Id, unitOfWork, cancellationToken);

    public Task<Result<RequestScope>> ResolveAsync(DeactivateAccount request, IUnitOfWork unitOfWork, CancellationToken cancellationToken) => ResolveAsync(NotNull(request).Id, unitOfWork, cancellationToken);

    public Task<Result<RequestScope>> ResolveAsync(ReactivateAccount request, IUnitOfWork unitOfWork, CancellationToken cancellationToken) => ResolveAsync(NotNull(request).Id, unitOfWork, cancellationToken);

    private static T NotNull<T>(T request)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        return request;
    }

    private static async Task<Result<RequestScope>> ResolveAsync(Guid id, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        AccountView? account = await AccountStore.FindAsync(unitOfWork, id, cancellationToken);
        return account is null ? Error.NotFound(OrganisationErrors.AccountNotFound, $"Account {id} does not exist.") : RequestScope.Installation;
    }
}

/// <summary>Shape checks of the account commands before the transaction (RR-51); never stored.</summary>
internal sealed class CreateAccountValidator : ICommandValidator<CreateAccount>
{
    public IEnumerable<Error> Validate(CreateAccount command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OrganisationValidation.RequiredId(command.ChartId, "chart", "A chart of accounts") is { } chart)
        {
            yield return chart;
        }

        if (OrganisationValidation.RequiredCode(command.AccountNumber, "account_number", "An account") is { } number)
        {
            yield return number;
        }

        foreach (Error error in AccountValidation.NameAndType(command.Name, command.AccountType))
        {
            yield return error;
        }
    }
}

internal sealed class UpdateAccountValidator : ICommandValidator<UpdateAccount>
{
    public IEnumerable<Error> Validate(UpdateAccount command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return AccountValidation.NameAndType(command.Name, command.AccountType);
    }
}

/// <summary>ACCOUNT.BLOCK's reason (D-69): required, at most 500 characters; an empty reason is 400 reason_required and nothing is stored.</summary>
internal sealed class BlockAccountValidator : ICommandValidator<BlockAccount>
{
    public IEnumerable<Error> Validate(BlockAccount command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            yield return Error.Validation(OrganisationErrors.ReasonRequired, "Blocking an account needs a reason.", details: AccountValidation.Field("reason"));
        }
        else if (command.Reason.Trim().Length > AccountValidation.ReasonMaxLength)
        {
            yield return Error.Validation(OrganisationErrors.ReasonTooLong, $"The reason holds at most {AccountValidation.ReasonMaxLength} characters.", details: AccountValidation.Field("reason"));
        }
    }
}

internal static class AccountValidation
{
    public const int ReasonMaxLength = 500;

    public static IEnumerable<Error> NameAndType(string? name, AccountType? type)
    {
        foreach (Error error in OrganisationValidation.Name(name, "name", "The name"))
        {
            yield return error;
        }

        if (type is null || !Enum.IsDefined(type.Value))
        {
            yield return Error.Validation("account_type_required", "An account type (Asset, Liability, Equity, Revenue or Expense) is required.", details: Field("accountType"));
        }
    }

    public static Dictionary<string, string> Field(string field) => new(StringComparer.Ordinal) { ["field"] = field };
}

/// <summary>GET one account (ACCOUNT.READ): 404 when it does not exist.</summary>
internal sealed class GetAccountHandler : IQueryHandler<GetAccount, AccountView>
{
    public async Task<Result<AccountView>> HandleAsync(GetAccount query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        AccountView? account = await AccountStore.FindAsync(context.UnitOfWork, query.Id, cancellationToken);
        return account is null ? Error.NotFound(OrganisationErrors.AccountNotFound, $"Account {query.Id} does not exist.") : account;
    }
}

/// <summary>GET a chart's accounts (ACCOUNT.READ), ordered by account number, at most OrganisationRoutes.ListLimit rows; a missing chart is 404.</summary>
internal sealed class ListAccountsHandler : IQueryHandler<ListAccounts, IReadOnlyList<AccountView>>
{
    public async Task<Result<IReadOnlyList<AccountView>>> HandleAsync(ListAccounts query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        if (await OrganisationStore.FindChartAsync(context.UnitOfWork, query.ChartId, cancellationToken) is null)
        {
            return Error.NotFound(OrganisationErrors.ChartNotFound, $"Chart of accounts {query.ChartId} does not exist.");
        }

        return Result<IReadOnlyList<AccountView>>.Success(await AccountStore.ListAsync(context.UnitOfWork, query.ChartId, OrganisationRoutes.ListLimit, cancellationToken));
    }
}

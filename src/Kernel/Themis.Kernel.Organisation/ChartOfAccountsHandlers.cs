using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// CHART_OF_ACCOUNTS.CREATE (milestone 2 step 3 brief, item 1): the header of an installation-level chart. The code
/// is unique per installation; a duplicate is rejected with R-MD-05, and two commands racing past the check meet
/// ux_chart_of_accounts_code, which the pipeline maps to the same rejection (RR-45). One audit event and one
/// CHART_OF_ACCOUNTS_CREATED event with an empty company, since a chart is an installation-scope aggregate (D-44, D-50).
/// </summary>
internal sealed class CreateChartOfAccountsHandler : ICommandHandler<CreateChartOfAccounts, ChartOfAccountsView>
{
    private readonly IOrganisationCreateSeam _seam;

    public CreateChartOfAccountsHandler(IOrganisationCreateSeam seam)
    {
        _seam = seam;
    }

    public async Task<Result<ChartOfAccountsView>> HandleAsync(CreateChartOfAccounts command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        // R-MD-05: the chart code is unique per installation (Specification, ChartOfAccounts).
        if (await OrganisationStore.ChartCodeExistsAsync(unitOfWork, command.Code, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.ChartCodeDuplicate, $"Chart of accounts {command.Code} already exists.");
        }

        await _seam.AfterDuplicateChecksAsync(OrganisationAudit.ChartEntity, command.Code, cancellationToken);

        ChartOfAccountsView chart = new(EntityId.New(), command.Code, command.Name.Trim(), OrganisationValidation.Trimmed(command.Template), Version: 1);
        await OrganisationStore.InsertChartAsync(unitOfWork, chart, context.Now, context.Principal.UserId, cancellationToken);
        context.Audit.Record(OrganisationAudit.ChartEntity, chart.Id, OrganisationAudit.ChartChanges(null, chart));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.ChartType, "CREATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.ChartType, chart.Id, companyId: null, chart);
        return chart;
    }
}

/// <summary>
/// CHART_OF_ACCOUNTS.UPDATE (step 3 brief, item 1): name and template on the current version (R-API-02, lock class 1
/// by the versioned update). The code never changes (R-MD-06): a differing code in the body is rejected, an equal
/// one ignored. One audit event and one CHART_OF_ACCOUNTS_UPDATED event.
/// </summary>
internal sealed class UpdateChartOfAccountsHandler : ICommandHandler<UpdateChartOfAccounts, ChartOfAccountsView>
{
    public async Task<Result<ChartOfAccountsView>> HandleAsync(UpdateChartOfAccounts command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        ChartOfAccountsView? before = await OrganisationStore.FindChartAsync(unitOfWork, command.Id, cancellationToken);
        if (before is null)
        {
            return Error.NotFound(OrganisationErrors.ChartNotFound, $"Chart of accounts {command.Id} does not exist.");
        }

        if (before.Version != command.ExpectedVersion)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Chart of accounts {command.Id} is at version {before.Version}, not {command.ExpectedVersion}.");
        }

        // R-MD-06: the code is fixed from creation (Specification, Fixed attributes).
        if (command.Code is { } code && code != before.Code)
        {
            return Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.ChartCodeFixed, $"The code of chart of accounts {before.Code} never changes (R-MD-06).");
        }

        ChartOfAccountsView after = before with { Name = command.Name.Trim(), Template = OrganisationValidation.Trimmed(command.Template), Version = before.Version + 1 };
        if (await OrganisationStore.UpdateChartAsync(unitOfWork, after, command.ExpectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Chart of accounts {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        IReadOnlyDictionary<string, FieldChange> changes = OrganisationAudit.ChartChanges(before, after);
        context.Audit.Record(OrganisationAudit.ChartEntity, after.Id, changes);
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.ChartType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.ChartType, after.Id, companyId: null, after);
        return after;
    }
}

/// <summary>The scope of a command on an existing chart: installation scope once the chart is loaded (Build Brief, Authorisation); a missing chart is 404.</summary>
internal sealed class ChartOfAccountsScopeResolver : IScopeResolver<UpdateChartOfAccounts>
{
    public async Task<Result<RequestScope>> ResolveAsync(UpdateChartOfAccounts request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ChartOfAccountsView? chart = await OrganisationStore.FindChartAsync(unitOfWork, request.Id, cancellationToken);
        return chart is null ? Error.NotFound(OrganisationErrors.ChartNotFound, $"Chart of accounts {request.Id} does not exist.") : RequestScope.Installation;
    }
}

internal sealed class CreateChartOfAccountsValidator : ICommandValidator<CreateChartOfAccounts>
{
    public IEnumerable<Error> Validate(CreateChartOfAccounts command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OrganisationValidation.RequiredCode(command.Code, "code", "A chart of accounts") is { } code)
        {
            yield return code;
        }

        foreach (Error error in OrganisationValidation.Name(command.Name, "name", "The name").Concat(OrganisationValidation.Name(command.Template, "template", "The template", required: false)))
        {
            yield return error;
        }
    }
}

internal sealed class UpdateChartOfAccountsValidator : ICommandValidator<UpdateChartOfAccounts>
{
    public IEnumerable<Error> Validate(UpdateChartOfAccounts command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return OrganisationValidation.Name(command.Name, "name", "The name").Concat(OrganisationValidation.Name(command.Template, "template", "The template", required: false));
    }
}

/// <summary>GET one chart (CHART_OF_ACCOUNTS.READ): 404 when it does not exist.</summary>
internal sealed class GetChartOfAccountsHandler : IQueryHandler<GetChartOfAccounts, ChartOfAccountsView>
{
    public async Task<Result<ChartOfAccountsView>> HandleAsync(GetChartOfAccounts query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        ChartOfAccountsView? chart = await OrganisationStore.FindChartAsync(context.UnitOfWork, query.Id, cancellationToken);
        return chart is null ? Error.NotFound(OrganisationErrors.ChartNotFound, $"Chart of accounts {query.Id} does not exist.") : chart;
    }
}

/// <summary>GET every chart (CHART_OF_ACCOUNTS.READ), ordered by code, at most OrganisationRoutes.ListLimit rows.</summary>
internal sealed class ListChartsOfAccountsHandler : IQueryHandler<ListChartsOfAccounts, IReadOnlyList<ChartOfAccountsView>>
{
    public async Task<Result<IReadOnlyList<ChartOfAccountsView>>> HandleAsync(ListChartsOfAccounts query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        return Result<IReadOnlyList<ChartOfAccountsView>>.Success(await OrganisationStore.ListChartsAsync(context.UnitOfWork, OrganisationRoutes.ListLimit, cancellationToken));
    }
}

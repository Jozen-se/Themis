using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// SITE.CREATE (milestone 2 step 3 brief, item 6): a site of the company in the body, the command's scope. The
/// company must exist (400, stored); the code is unique per company (R-MD-05, 422; ux_site_company_code catches a
/// race, RR-45). The time zone is optional and an IANA name; empty means the company's (Specification, Site). One
/// audit event and one SITE_CREATED event with the company (D-44, D-50).
/// </summary>
internal sealed class CreateSiteHandler : ICommandHandler<CreateSite, SiteInfo>
{
    private readonly IOrganisationCreateSeam _seam;

    public CreateSiteHandler(IOrganisationCreateSeam seam)
    {
        _seam = seam;
    }

    public async Task<Result<SiteInfo>> HandleAsync(CreateSite command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        if (await OrganisationStore.FindCompanyAsync(unitOfWork, command.CompanyId, cancellationToken) is null)
        {
            return Error.Validation(OrganisationErrors.CompanyUnknown, $"Company {command.CompanyId} does not exist.");
        }

        // R-MD-05: the site code is unique per company (Specification, Site).
        if (await OrganisationStore.SiteCodeExistsAsync(unitOfWork, command.CompanyId, command.Code, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.SiteCodeDuplicate, $"Site code {command.Code} already exists in the company.");
        }

        await _seam.AfterDuplicateChecksAsync(OrganisationAudit.SiteEntity, command.Code, cancellationToken);

        SiteInfo site = new(EntityId.New(), command.CompanyId, command.Code, command.Name.Trim(), OrganisationValidation.Trimmed(command.TimeZone), Version: 1);
        await OrganisationStore.InsertSiteAsync(unitOfWork, site, context.Now, context.Principal.UserId, cancellationToken);
        context.Audit.Record(OrganisationAudit.SiteEntity, site.Id, OrganisationAudit.SiteChanges(null, site));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.SiteType, "CREATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.SiteType, site.Id, site.CompanyId, site);
        return site;
    }
}

/// <summary>SITE.UPDATE (step 3 brief, item 6): name and time zone on the current version (R-API-02, lock class 1); the code never changes (R-MD-06).</summary>
internal sealed class UpdateSiteHandler : ICommandHandler<UpdateSite, SiteInfo>
{
    public async Task<Result<SiteInfo>> HandleAsync(UpdateSite command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        SiteInfo? before = await OrganisationStore.FindSiteAsync(unitOfWork, command.Id, companyId: null, cancellationToken);
        if (before is null)
        {
            return Error.NotFound(OrganisationErrors.SiteNotFound, $"Site {command.Id} does not exist.");
        }

        if (before.Version != command.ExpectedVersion)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Site {command.Id} is at version {before.Version}, not {command.ExpectedVersion}.");
        }

        // R-MD-06: the code is fixed from creation.
        if (command.Code is { } code && code != before.Code)
        {
            return Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.SiteCodeFixed, $"The code of site {before.Code} never changes (R-MD-06).");
        }

        SiteInfo after = before with { Name = command.Name.Trim(), TimeZone = OrganisationValidation.Trimmed(command.TimeZone), Version = before.Version + 1 };
        if (await OrganisationStore.UpdateSiteAsync(unitOfWork, after, command.ExpectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Site {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        context.Audit.Record(OrganisationAudit.SiteEntity, after.Id, OrganisationAudit.SiteChanges(before, after));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.SiteType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.SiteType, after.Id, after.CompanyId, after);
        return after;
    }
}

/// <summary>The scope of SITE.UPDATE: the site's company, once loaded; a missing site is 404.</summary>
internal sealed class UpdateSiteScopeResolver : IScopeResolver<UpdateSite>
{
    public async Task<Result<RequestScope>> ResolveAsync(UpdateSite request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SiteInfo? site = await OrganisationStore.FindSiteAsync(unitOfWork, request.Id, companyId: null, cancellationToken);
        return site is null ? Error.NotFound(OrganisationErrors.SiteNotFound, $"Site {request.Id} does not exist.") : RequestScope.ForCompany(site.CompanyId);
    }
}

internal sealed class CreateSiteValidator : ICommandValidator<CreateSite>
{
    public IEnumerable<Error> Validate(CreateSite command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OrganisationValidation.RequiredId(command.CompanyId, "company", "A site's company") is { } company)
        {
            yield return company;
        }

        if (OrganisationValidation.RequiredCode(command.Code, "code", "A site") is { } code)
        {
            yield return code;
        }

        foreach (Error error in OrganisationValidation.Name(command.Name, "name", "The name").Concat(OrganisationValidation.TimeZone(command.TimeZone, "time_zone", required: false)))
        {
            yield return error;
        }
    }
}

internal sealed class UpdateSiteValidator : ICommandValidator<UpdateSite>
{
    public IEnumerable<Error> Validate(UpdateSite command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return OrganisationValidation.Name(command.Name, "name", "The name").Concat(OrganisationValidation.TimeZone(command.TimeZone, "time_zone", required: false));
    }
}

/// <summary>GET one site in a company (SITE.READ): 404 when it does not exist there.</summary>
internal sealed class GetSiteHandler : IQueryHandler<GetSite, SiteInfo>
{
    public async Task<Result<SiteInfo>> HandleAsync(GetSite query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        SiteInfo? site = await OrganisationStore.FindSiteAsync(context.UnitOfWork, query.Id, query.CompanyId, cancellationToken);
        return site is null ? Error.NotFound(OrganisationErrors.SiteNotFound, $"Site {query.Id} does not exist in company {query.CompanyId}.") : site;
    }
}

/// <summary>GET the sites of a company (SITE.READ), ordered by code, at most OrganisationRoutes.ListLimit rows.</summary>
internal sealed class ListSitesHandler : IQueryHandler<ListSites, IReadOnlyList<SiteInfo>>
{
    public async Task<Result<IReadOnlyList<SiteInfo>>> HandleAsync(ListSites query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        return Result<IReadOnlyList<SiteInfo>>.Success(await OrganisationStore.ListSitesAsync(context.UnitOfWork, query.CompanyId, OrganisationRoutes.ListLimit, cancellationToken));
    }
}

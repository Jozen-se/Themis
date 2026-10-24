using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// WAREHOUSE.CREATE (milestone 2 step 3 brief, item 7): a warehouse of the site in the body; the scope resolver
/// derived the company and the site from the site row, so the stored company is always the site's (Conventions,
/// Company scope). The code is unique per company (R-MD-05, 422; ux_warehouse_company_code catches a race, RR-45);
/// status Active. Making it the default moves the site's default: the site row is locked first, then the site's
/// warehouses in id order, the previous default is cleared before the new one is written, because the partial unique
/// index ux_warehouse_site_default cannot wait for the end of the command, and the previous default gets its new
/// version, audit event and WAREHOUSE_UPDATED event (Specification, Organisation commands; Lock order, 1b).
/// </summary>
internal sealed class CreateWarehouseHandler : ICommandHandler<CreateWarehouse, WarehouseView>
{
    private readonly IOrganisationCreateSeam _seam;

    public CreateWarehouseHandler(IOrganisationCreateSeam seam)
    {
        _seam = seam;
    }

    public async Task<Result<WarehouseView>> HandleAsync(CreateWarehouse command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        SiteInfo? site = await OrganisationStore.FindSiteAsync(unitOfWork, command.SiteId, companyId: null, cancellationToken);
        if (site is null)
        {
            return Error.Validation(OrganisationErrors.SiteUnknown, $"Site {command.SiteId} does not exist.");
        }

        // R-MD-05: the warehouse code is unique per company (Specification, Warehouse).
        if (await OrganisationStore.WarehouseCodeExistsAsync(unitOfWork, site.CompanyId, command.Code, cancellationToken))
        {
            return Error.Rule(OrganisationErrors.RuleUnique, OrganisationErrors.WarehouseCodeDuplicate, $"Warehouse code {command.Code} already exists in the company.");
        }

        await _seam.AfterDuplicateChecksAsync(OrganisationAudit.WarehouseEntity, command.Code, cancellationToken);

        if (command.IsDefault)
        {
            await WarehouseDefaults.MoveAsync(unitOfWork, context, site.Id, keep: null, cancellationToken);
        }

        WarehouseView warehouse = new(EntityId.New(), site.Id, site.CompanyId, command.Code, command.Name.Trim(), command.IsDefault, MasterDataStatus.Active, Version: 1);
        await OrganisationStore.InsertWarehouseAsync(unitOfWork, warehouse, context.Now, context.Principal.UserId, cancellationToken);
        context.Audit.Record(OrganisationAudit.WarehouseEntity, warehouse.Id, OrganisationAudit.WarehouseChanges(null, warehouse));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.WarehouseType, "CREATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.WarehouseType, warehouse.Id, warehouse.CompanyId, warehouse);
        return warehouse;
    }
}

/// <summary>
/// WAREHOUSE.UPDATE (step 3 brief, item 7): name and default flag on the current version; the site and the code never
/// change (R-MD-06). Only an Active warehouse becomes the default (a guard without a rule of its own, R-DOC-01), a
/// default change takes the site lock and the warehouse rows in id order before the versioned update, and clearing
/// the flag leaves the site without a default (Specification, Organisation commands).
/// </summary>
internal sealed class UpdateWarehouseHandler : ICommandHandler<UpdateWarehouse, WarehouseView>
{
    public async Task<Result<WarehouseView>> HandleAsync(UpdateWarehouse command, ICommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        IUnitOfWork unitOfWork = context.UnitOfWork;

        WarehouseView? before = await OrganisationStore.FindWarehouseAsync(unitOfWork, command.Id, companyId: null, cancellationToken);
        if (before is null)
        {
            return Error.NotFound(OrganisationErrors.WarehouseNotFound, $"Warehouse {command.Id} does not exist.");
        }

        if (before.Version != command.ExpectedVersion)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Warehouse {command.Id} is at version {before.Version}, not {command.ExpectedVersion}.");
        }

        // R-MD-06: the code is fixed from creation.
        if (command.Code is { } code && code != before.Code)
        {
            return Error.Rule(OrganisationErrors.RuleFixed, OrganisationErrors.WarehouseCodeFixed, $"The code of warehouse {before.Code} never changes (R-MD-06).");
        }

        bool becomesDefault = command.IsDefault && !before.IsDefault;
        if (becomesDefault && before.Status != MasterDataStatus.Active)
        {
            return Error.Rule(OrganisationErrors.RuleGuard, OrganisationErrors.WarehouseNotActive, $"Warehouse {before.Code} is {before.Status}; only an Active warehouse can be the site's default.");
        }

        if (becomesDefault)
        {
            // Lock order, 1b: the site row, then the site's warehouses in id order (the own row among them), then the versioned update.
            await WarehouseDefaults.MoveAsync(unitOfWork, context, before.SiteId, keep: before.Id, cancellationToken);
        }

        WarehouseView after = before with { Name = command.Name.Trim(), IsDefault = command.IsDefault, Version = before.Version + 1 };
        if (await OrganisationStore.UpdateWarehouseAsync(unitOfWork, after, command.ExpectedVersion, context.Now, context.Principal.UserId, cancellationToken) != 1)
        {
            return Error.PreconditionFailed(OrganisationErrors.VersionStale, $"Warehouse {command.Id} changed since version {command.ExpectedVersion} was read.");
        }

        context.Audit.Record(OrganisationAudit.WarehouseEntity, after.Id, OrganisationAudit.WarehouseChanges(before, after));
        context.Outbox.Publish(EventNames.Generated(OrganisationAudit.WarehouseType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.WarehouseType, after.Id, after.CompanyId, after);
        return after;
    }
}

/// <summary>Moving a site's default warehouse (Specification, Organisation commands; Lock order, 1b).</summary>
internal static class WarehouseDefaults
{
    /// <summary>
    /// Locks the site row, then the site's warehouses in id order in one statement, and clears the previous default
    /// (every default but <paramref name="keep"/>) with a new version, its audit event and its WAREHOUSE_UPDATED event,
    /// before the caller writes the new default. The partial unique index stays as the backstop (RR-45).
    /// </summary>
    public static async Task MoveAsync(IUnitOfWork unitOfWork, ICommandContext context, Guid siteId, Guid? keep, CancellationToken cancellationToken)
    {
        await OrganisationStore.LockSiteAsync(unitOfWork, siteId, cancellationToken);
        IReadOnlyList<WarehouseView> locked = await OrganisationStore.LockSiteWarehousesAsync(unitOfWork, siteId, cancellationToken);
        foreach (WarehouseView previous in locked.Where(warehouse => warehouse.IsDefault && warehouse.Id != keep))
        {
            WarehouseView cleared = previous with { IsDefault = false, Version = previous.Version + 1 };
            if (await OrganisationStore.UpdateWarehouseAsync(unitOfWork, cleared, previous.Version, context.Now, context.Principal.UserId, cancellationToken) != 1)
            {
                throw new InvalidOperationException($"Warehouse {previous.Id} changed while its row was locked.");
            }

            // The previous default is a changed row: its own audit event and event (R-AUD-01, D-44); a holder of its old version gets 412 later.
            context.Audit.Record(OrganisationAudit.WarehouseEntity, cleared.Id, OrganisationAudit.WarehouseChanges(previous, cleared));
            context.Outbox.Publish(EventNames.Generated(OrganisationAudit.WarehouseType, "UPDATE"), EventNames.DefaultSchemaVersion, OrganisationAudit.WarehouseType, cleared.Id, cleared.CompanyId, cleared);
        }
    }
}

/// <summary>
/// The scopes of the warehouse requests, each from the loaded row (Build Brief, Authorisation): a create from its
/// site (the site's company and id, never a company id in the body), an update or a read from the warehouse's site,
/// so a site-limited assignment acts on its own site's warehouses. A missing row is 404, never a scope.
/// </summary>
internal sealed class WarehouseScopeResolver : IScopeResolver<CreateWarehouse>, IScopeResolver<UpdateWarehouse>, IScopeResolver<GetWarehouse>
{
    public async Task<Result<RequestScope>> ResolveAsync(CreateWarehouse request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SiteInfo? site = await OrganisationStore.FindSiteAsync(unitOfWork, request.SiteId, companyId: null, cancellationToken);
        return site is null ? Error.NotFound(OrganisationErrors.SiteNotFound, $"Site {request.SiteId} does not exist.") : RequestScope.ForSite(site.CompanyId, site.Id);
    }

    public async Task<Result<RequestScope>> ResolveAsync(UpdateWarehouse request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WarehouseView? warehouse = await OrganisationStore.FindWarehouseAsync(unitOfWork, request.Id, companyId: null, cancellationToken);
        return warehouse is null ? Error.NotFound(OrganisationErrors.WarehouseNotFound, $"Warehouse {request.Id} does not exist.") : RequestScope.ForSite(warehouse.CompanyId, warehouse.SiteId);
    }

    public async Task<Result<RequestScope>> ResolveAsync(GetWarehouse request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WarehouseView? warehouse = await OrganisationStore.FindWarehouseAsync(unitOfWork, request.Id, request.CompanyId, cancellationToken);
        return warehouse is null ? Error.NotFound(OrganisationErrors.WarehouseNotFound, $"Warehouse {request.Id} does not exist in company {request.CompanyId}.") : RequestScope.ForSite(warehouse.CompanyId, warehouse.SiteId);
    }
}

internal sealed class CreateWarehouseValidator : ICommandValidator<CreateWarehouse>
{
    public IEnumerable<Error> Validate(CreateWarehouse command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (OrganisationValidation.RequiredId(command.SiteId, "site", "A warehouse's site") is { } site)
        {
            yield return site;
        }

        if (OrganisationValidation.RequiredCode(command.Code, "code", "A warehouse") is { } code)
        {
            yield return code;
        }

        foreach (Error error in OrganisationValidation.Name(command.Name, "name", "The name"))
        {
            yield return error;
        }
    }
}

internal sealed class UpdateWarehouseValidator : ICommandValidator<UpdateWarehouse>
{
    public IEnumerable<Error> Validate(UpdateWarehouse command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return OrganisationValidation.Name(command.Name, "name", "The name");
    }
}

/// <summary>GET one warehouse in a company (WAREHOUSE.READ): the scope resolver found it, so only a race can make it 404 here.</summary>
internal sealed class GetWarehouseHandler : IQueryHandler<GetWarehouse, WarehouseView>
{
    public async Task<Result<WarehouseView>> HandleAsync(GetWarehouse query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        WarehouseView? warehouse = await OrganisationStore.FindWarehouseAsync(context.UnitOfWork, query.Id, query.CompanyId, cancellationToken);
        return warehouse is null ? Error.NotFound(OrganisationErrors.WarehouseNotFound, $"Warehouse {query.Id} does not exist in company {query.CompanyId}.") : warehouse;
    }
}

/// <summary>GET the warehouses of a company, or of one of its sites (WAREHOUSE.READ), ordered by code, at most OrganisationRoutes.ListLimit rows; a site outside the company is 404.</summary>
internal sealed class ListWarehousesHandler : IQueryHandler<ListWarehouses, IReadOnlyList<WarehouseView>>
{
    public async Task<Result<IReadOnlyList<WarehouseView>>> HandleAsync(ListWarehouses query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        Guid? site = query.SiteId is { } given && given != Guid.Empty ? given : null;
        if (site is { } siteId && await OrganisationStore.FindSiteAsync(context.UnitOfWork, siteId, query.CompanyId, cancellationToken) is null)
        {
            return Error.NotFound(OrganisationErrors.SiteNotFound, $"Site {siteId} does not exist in company {query.CompanyId}.");
        }

        return Result<IReadOnlyList<WarehouseView>>.Success(await OrganisationStore.ListWarehousesAsync(context.UnitOfWork, query.CompanyId, site, OrganisationRoutes.ListLimit, cancellationToken));
    }
}

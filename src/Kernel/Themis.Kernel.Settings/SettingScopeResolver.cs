using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Settings;

/// <summary>
/// The scope of SETTING.SET, SETTING.CLEAR and SETTING.READ, one resolver for every level, run inside the request's
/// transaction like other requests on existing records (Build Brief, Authorisation; D-47). Installation declares
/// installation scope (RR-52); Company the company; Site the company and site; Warehouse the warehouse's company and
/// site, read from the warehouse row (D-63, RR-102). A site or warehouse that does not exist, or that belongs to
/// another company than the body names, declares the body's company: the handler's target check then answers 400 for
/// a write, stored as the key's result, and 404 for a read, which a resolver failure would not give. The site serves
/// the scope only and is never stored on a warehouse value (ck_setting_value_target).
/// </summary>
internal sealed class SettingScopeResolver : IScopeResolver<SetSetting>, IScopeResolver<ClearSetting>, IScopeResolver<GetSetting>
{
    public Task<Result<RequestScope>> ResolveAsync(SetSetting request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ForLevelAsync(request.Level, request.CompanyId, request.SiteId, request.WarehouseId, unitOfWork, cancellationToken);
    }

    public Task<Result<RequestScope>> ResolveAsync(ClearSetting request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ForLevelAsync(request.Level, request.CompanyId, request.SiteId, request.WarehouseId, unitOfWork, cancellationToken);
    }

    /// <summary>A read names its target, not a level: the most specific target given decides, as for a write at that level.</summary>
    public Task<Result<RequestScope>> ResolveAsync(GetSetting request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        SettingLevel level = request switch
        {
            { CompanyId: null } => SettingLevel.Installation,
            { WarehouseId: not null } => SettingLevel.Warehouse,
            { SiteId: not null } => SettingLevel.Site,
            _ => SettingLevel.Company,
        };
        return ForLevelAsync(level, request.CompanyId, request.SiteId, request.WarehouseId, unitOfWork, cancellationToken);
    }

    private static async Task<Result<RequestScope>> ForLevelAsync(SettingLevel level, Guid? companyId, Guid? siteId, Guid? warehouseId, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        // The validators ran first (RR-51): a Company, Site or Warehouse write names a company that is not empty.
        if (level == SettingLevel.Installation || companyId is not { } company || company == Guid.Empty)
        {
            return RequestScope.Installation;
        }

        switch (level)
        {
            case SettingLevel.Site when siteId is { } site && site != Guid.Empty:
                return await SettingStore.SiteCompanyAsync(unitOfWork, site, cancellationToken) == company
                    ? RequestScope.ForSite(company, site)
                    : RequestScope.ForCompany(company);
            case SettingLevel.Warehouse when warehouseId is { } warehouse && warehouse != Guid.Empty:
                (Guid SiteId, Guid CompanyId)? found = await SettingStore.WarehouseSiteAsync(unitOfWork, warehouse, cancellationToken);
                return found is { } row && row.CompanyId == company
                    ? RequestScope.ForSite(company, row.SiteId)
                    : RequestScope.ForCompany(company);
            default:
                return RequestScope.ForCompany(company);
        }
    }
}

using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.MasterData;

/// <summary>
/// ITEM.CREATE (step 5 brief): creates the installation-wide Item and its ItemCompany row for the company in the
/// body, in one command (D-43). The company in the body is the command's scope (R-SEC-01). The item number is
/// stored uppercase; a duplicate is rejected with R-MD-05. Default sales and purchase units are not part of any
/// command in milestone 1.
/// </summary>
[Command("ITEM.CREATE")]
public sealed record CreateItem(
    Guid CompanyId,
    Code ItemNumber,
    string Description,
    ItemType ItemType,
    UnitCode BaseUnit,
    LotTracking LotTracking,
    Code ItemPostingGroup,
    TaxClass TaxClass,
    CostingMethod CostingMethod,
    bool SalesAllowed = true,
    bool PurchaseAllowed = true,
    Guid? DefaultWarehouseId = null,
    decimal? NetWeightKg = null,
    decimal? SalesMultiple = null,
    decimal? MinOrderQty = null) : ICommand<ItemView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

/// <summary>The company's row of an item (D-43), as the API returns it.</summary>
public sealed record ItemCompanyView(
    Guid Id,
    Guid CompanyId,
    MasterDataStatus Status,
    Code ItemPostingGroup,
    TaxClass TaxClass,
    CostingMethod CostingMethod,
    bool SalesAllowed,
    bool PurchaseAllowed,
    Guid? DefaultWarehouseId,
    long Version);

/// <summary>An item with the row of the company it was read or created in; decimals travel as strings (D-37).</summary>
public sealed record ItemView(
    Guid Id,
    Code ItemNumber,
    string Description,
    ItemType ItemType,
    UnitCode BaseUnit,
    LotTracking LotTracking,
    decimal? NetWeightKg,
    decimal? SalesMultiple,
    decimal? MinOrderQty,
    long Version,
    ItemCompanyView Company) : IVersionedResult;

/// <summary>GET /v1/kernel/companies/{companyId}/items/{id} (ITEM.READ, D-47): the scope is the company in the route, checked before anything is loaded.</summary>
[Query("ITEM.READ")]
public sealed record GetItem(Guid CompanyId, Guid Id) : IQuery<ItemView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

/// <summary>GET /v1/kernel/companies/{companyId}/items/by-number/{itemNumber} (ITEM.READ, D-47): the same read by item number.</summary>
[Query("ITEM.READ")]
public sealed record GetItemByNumber(Guid CompanyId, Code ItemNumber) : IQuery<ItemView>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

public static class MasterDataRoutes
{
    public const string Items = "/v1/kernel/items";
    public const string Item = "/v1/kernel/companies/{companyId}/items/{id}";
    public const string ItemByNumber = "/v1/kernel/companies/{companyId}/items/by-number/{itemNumber}";

    /// <summary>The command routes the composition root maps through the kernel pipeline (brief, API conventions).</summary>
    public static IReadOnlyList<ICommandRoute> Commands { get; } =
    [
        new CommandRoute<CreateItem, ItemView>(Items),
    ];
}

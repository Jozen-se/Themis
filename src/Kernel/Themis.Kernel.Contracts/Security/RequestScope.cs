using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Security;

/// <summary>Where a command or query acts: the whole installation, one company, or one site of a company (R-SEC-01, D-47).</summary>
public enum ScopeKind
{
    Installation,
    Company,
    Site,
}

/// <summary>
/// The scope a command or query declares from its own data (Build Brief, Authorisation): a create command carries
/// the company in its body, a command on an existing record derives it from the record, an installation-wide one
/// declares installation scope. The pipeline authorises against it after the record is loaded, inside the
/// transaction, before any business logic; a request without a scope is refused (D-47).
/// </summary>
public readonly record struct RequestScope
{
    private RequestScope(ScopeKind kind, Guid? companyId, Guid? siteId)
    {
        Kind = kind;
        CompanyId = companyId;
        SiteId = siteId;
    }

    public ScopeKind Kind { get; }

    public Guid? CompanyId { get; }

    public Guid? SiteId { get; }

    /// <summary>Installation-wide: user, role and installation commands, and reads that name no company.</summary>
    public static RequestScope Installation { get; } = new(ScopeKind.Installation, null, null);

    public static RequestScope ForCompany(Guid companyId) =>
        companyId == Guid.Empty ? throw new ArgumentException("A company scope names a company.", nameof(companyId)) : new(ScopeKind.Company, companyId, null);

    public static RequestScope ForSite(Guid companyId, Guid siteId) =>
        companyId == Guid.Empty || siteId == Guid.Empty
            ? throw new ArgumentException("A site scope names a company and a site.")
            : new(ScopeKind.Site, companyId, siteId);

    public override string ToString() => Kind switch
    {
        ScopeKind.Installation => "installation",
        ScopeKind.Company => $"company {CompanyId}",
        _ => $"company {CompanyId}, site {SiteId}",
    };
}

/// <summary>A command or query whose scope is in its own data: the company (and site) it names in the body or route.</summary>
public interface IScopedRequest
{
    RequestScope Scope { get; }
}

/// <summary>
/// Derives the scope of a command or query on an existing record by loading the record on the request's unit of
/// work (Build Brief, Authorisation). Registered per request type; a missing record is a 404, never a scope.
/// </summary>
public interface IScopeResolver<in TRequest>
{
    Task<Result<RequestScope>> ResolveAsync(TRequest request, IUnitOfWork unitOfWork, CancellationToken cancellationToken);
}

using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>GET /v1/kernel/companies/{id} (COMPANY.READ): the company, or 404 when it does not exist.</summary>
internal sealed class GetCompanyHandler : IQueryHandler<GetCompany, CompanyInfo>
{
    private readonly IOrganisationReader _reader;

    public GetCompanyHandler(IOrganisationReader reader)
    {
        _reader = reader;
    }

    public async Task<Result<CompanyInfo>> HandleAsync(GetCompany query, IQueryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        CompanyInfo? company = await _reader.GetCompanyAsync(context.UnitOfWork, query.Id, cancellationToken);
        return company is null ? Error.NotFound("company_not_found", $"Company {query.Id} does not exist.") : company;
    }
}

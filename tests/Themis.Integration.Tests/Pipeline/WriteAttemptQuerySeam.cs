using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// The write-attempting query seam of RR-81 (docs/development/test-seams.md): a test-only query under the test-only
/// FIXTURE.READ whose handler tries to write the fixture table as themis_fixture. The query dispatcher's transaction
/// is read only, so PostgreSQL refuses the statement with 25006 and the query answers 500. Mapped by PipelineTestHost only.
/// </summary>
[Query(FixtureContracts.ReadPermission)]
internal sealed record WriteAttemptQuery(Guid CompanyId) : IQuery<long>, IScopedRequest
{
    public RequestScope Scope => RequestScope.ForCompany(CompanyId);
}

internal static class WriteAttemptQueryRoutes
{
    public const string Query = "/v1/test/write-attempt";
}

internal sealed class WriteAttemptQueryHandler : IQueryHandler<WriteAttemptQuery, long>
{
    private static readonly DatabaseRole Fixture = DatabaseRole.From(FixtureContracts.RoleName);

    public async Task<Result<long>> HandleAsync(WriteAttemptQuery query, IQueryContext context, CancellationToken cancellationToken) =>
        await context.UnitOfWork.ExecuteAsync(Fixture, $"UPDATE {FixtureContracts.SchemaName}.counter SET value = value + 1 WHERE company_id = @company", new Dictionary<string, object?> { ["company"] = query.CompanyId }, cancellationToken);
}

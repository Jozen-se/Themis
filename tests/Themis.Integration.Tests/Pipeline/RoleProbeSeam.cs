using Npgsql;
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Persistence.UnitOfWork;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// The RR-62 dispatcher seam (docs/development/test-seams.md): a test-only command whose scope resolver and handler
/// read <c>SELECT current_user</c> straight on the persistence-internal connection, without switching, as
/// UnitOfWorkRoleTests does. The dispatcher resets the role before each, so both must see themis_app, also after the
/// kernel's advisory lock and idempotency look-up ran as themis_kernel just before the handler. Mapped by
/// PipelineTestHost only; runs under the test-only FIXTURE.TOUCH.
/// </summary>
[Command(FixtureContracts.TouchPermission)]
internal sealed record RoleProbe(Guid CompanyId) : ICommand<RoleProbeResult>;

internal sealed record RoleProbeResult(string ScopeResolverUser, string HandlerUser);

internal static class RoleProbeRoutes
{
    public const string Probe = "/v1/test/role-probe";

    public static IReadOnlyList<ICommandRoute> Commands { get; } = [new CommandRoute<RoleProbe, RoleProbeResult>(Probe)];

    /// <summary>The login role's view, read on the unit of work's own connection with no role switch (persistence-internal).</summary>
    public static async Task<string> CurrentUserAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        NpgsqlUnitOfWork npgsql = (NpgsqlUnitOfWork)unitOfWork;
        await using NpgsqlCommand command = new("SELECT current_user", (NpgsqlConnection)npgsql.Connection, (NpgsqlTransaction)npgsql.Transaction);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

/// <summary>Remembers what the scope resolver saw, for the handler to report.</summary>
internal sealed class RoleProbeRecorder
{
    public string? ScopeResolverUser { get; set; }
}

internal sealed class RoleProbeScopeResolver : IScopeResolver<RoleProbe>
{
    private readonly RoleProbeRecorder _recorder;

    public RoleProbeScopeResolver(RoleProbeRecorder recorder)
    {
        _recorder = recorder;
    }

    public async Task<Result<RequestScope>> ResolveAsync(RoleProbe request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        _recorder.ScopeResolverUser = await RoleProbeRoutes.CurrentUserAsync(unitOfWork, cancellationToken);
        return RequestScope.ForCompany(request.CompanyId);
    }
}

internal sealed class RoleProbeHandler : ICommandHandler<RoleProbe, RoleProbeResult>
{
    private readonly RoleProbeRecorder _recorder;

    public RoleProbeHandler(RoleProbeRecorder recorder)
    {
        _recorder = recorder;
    }

    public async Task<Result<RoleProbeResult>> HandleAsync(RoleProbe command, ICommandContext context, CancellationToken cancellationToken) =>
        new RoleProbeResult(_recorder.ScopeResolverUser ?? "not called", await RoleProbeRoutes.CurrentUserAsync(context.UnitOfWork, cancellationToken));
}

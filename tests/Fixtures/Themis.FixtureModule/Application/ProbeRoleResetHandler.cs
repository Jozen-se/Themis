using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Domain;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.FixtureModule.Application;

/// <summary>Reads a counter on a connection taken before a call into the kernel (RR-62); the kernel call runs between the two.</summary>
internal interface IRoleProbeStore
{
    Task<Counter?> ReadAfterKernelCallAsync(IUnitOfWork unitOfWork, Guid id, bool throughEntityFramework, Func<CancellationToken, Task> kernelCall, CancellationToken cancellationToken);
}

/// <summary>
/// The RR-62 probe: the store takes its connection through ForRoleAsync, the settings reader runs and hands the
/// connection back as themis_app, then the store reads the counter. Writes nothing, records no audit or outbox event.
/// </summary>
internal sealed class ProbeRoleResetHandler : ICommandHandler<ProbeRoleReset, CounterSnapshot>
{
    private readonly IRoleProbeStore _store;
    private readonly ISettingsReader _settings;

    public ProbeRoleResetHandler(IRoleProbeStore store, ISettingsReader settings)
    {
        _store = store;
        _settings = settings;
    }

    public async Task<Result<CounterSnapshot>> HandleAsync(ProbeRoleReset command, ICommandContext context, CancellationToken cancellationToken)
    {
        Counter? counter = await _store.ReadAfterKernelCallAsync(
            context.UnitOfWork,
            command.Id,
            command.ThroughEntityFramework,
            ct => _settings.GetIntegerAsync(context.UnitOfWork, SettingKeys.IdempotencyRetentionDays, SettingTarget.Installation, ct),
            cancellationToken);
        return counter is null
            ? Error.NotFound("counter_not_found", $"Counter {command.Id} does not exist.")
            : CreateCounterHandler.Snapshot(counter);
    }
}

/// <summary>The probe's scope is the counter's (Build Brief, Authorisation); a missing counter is 404.</summary>
internal sealed class ProbeRoleResetScopeResolver : IScopeResolver<ProbeRoleReset>
{
    private readonly ICounterStore _store;

    public ProbeRoleResetScopeResolver(ICounterStore store)
    {
        _store = store;
    }

    public async Task<Result<RequestScope>> ResolveAsync(ProbeRoleReset request, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        Counter? counter = await _store.FindAsync(unitOfWork, request.Id, cancellationToken);
        if (counter is null)
        {
            return Error.NotFound("counter_not_found", $"Counter {request.Id} does not exist.");
        }

        return counter.SiteId is { } site ? RequestScope.ForSite(counter.CompanyId, site) : RequestScope.ForCompany(counter.CompanyId);
    }
}

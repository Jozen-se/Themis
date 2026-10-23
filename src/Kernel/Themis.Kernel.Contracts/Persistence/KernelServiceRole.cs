namespace Themis.Kernel.Contracts.Persistence;

/// <summary>
/// The one way a kernel Contracts service hands the connection back (RR-62, R-DB-01): on successful return it resets
/// the role, so the caller's next statement that does not switch runs as themis_app and is refused. Used by
/// IOrganisationReader, ISettingsReader, IPostingService, INumberSeriesService, IAccountResolver and IAccessControl,
/// and by any Contracts service added later; not by IIdempotencyStore, IAuditStore or IOutboxStore, which only the
/// pipeline calls. A service that threw, or that caught a database error, never comes here: the transaction is
/// aborted and the reset would raise 25P02 over the original error.
/// </summary>
internal static class KernelServiceRole
{
    public static async Task<T> ThenResetRoleAsync<T>(this Task<T> call, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        T value = await call;
        await ResetAsync(unitOfWork, cancellationToken);
        return value;
    }

    public static Task ResetAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken) =>
        unitOfWork is IKernelUnitOfWork kernel
            ? kernel.ResetRoleAsync(cancellationToken)
            : throw new InvalidOperationException($"{unitOfWork?.GetType().FullName ?? "null"} is not the kernel's unit of work; a kernel service cannot hand its connection back (RR-62).");
}

namespace Themis.Kernel.Contracts.Commands;

/// <summary>A named business command (P-04). The result type is what the API returns on success.</summary>
#pragma warning disable CA1040 // marker interface: the type parameter carries the contract
public interface ICommand<TResult>
#pragma warning restore CA1040
{
}

/// <summary>
/// A command that changes an existing record and therefore carries the record version from the <c>If-Match</c>
/// header (R-API-02). The HTTP endpoint requires the header and writes its value into the command's
/// <c>expectedVersion</c> JSON property before binding, so the body never supplies it; the handler compares it with
/// the row's version and rejects a stale one with 412.
/// </summary>
public interface IVersionedCommand
{
    long ExpectedVersion { get; }
}

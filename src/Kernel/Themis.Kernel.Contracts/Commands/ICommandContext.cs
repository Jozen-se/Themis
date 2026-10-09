using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Commands;

/// <summary>What a handler sees of the pipeline: who runs the command, which execution it is, the one unit of work, and the audit and outbox writers.</summary>
public interface ICommandContext
{
    CommandId CommandId { get; }

    CorrelationId CorrelationId { get; }

    Principal Principal { get; }

    IdempotencyKey IdempotencyKey { get; }

    /// <summary>System time in UTC from the injected clock (R-TIME-01). Business dates come from the command itself.</summary>
    DateTimeOffset Now { get; }

    IUnitOfWork UnitOfWork { get; }

    IAuditWriter Audit { get; }

    IOutboxWriter Outbox { get; }
}

using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Commands;

/// <summary>
/// Handles one command inside the pipeline's single transaction (R-LED-05, D-41). The handler reaches the database
/// only through <see cref="ICommandContext.UnitOfWork"/>, records every changed entity through
/// <see cref="ICommandContext.Audit"/> (R-AUD-01) and every state change through <see cref="ICommandContext.Outbox"/> (D-44).
/// A rejected command returns a failed <see cref="Result{T}"/> with the rule that failed (R-API-03); it never throws for a business rule.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, ICommandContext context, CancellationToken cancellationToken);
}

/// <summary>Validates a command before authorisation and before any transaction starts.</summary>
public interface ICommandValidator<in TCommand>
{
    IEnumerable<Error> Validate(TCommand command);
}

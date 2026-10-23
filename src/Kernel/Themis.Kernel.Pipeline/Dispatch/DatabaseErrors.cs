using System.Data.Common;
using Npgsql;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// What the pipeline reads out of a database failure, wherever it sits in the exception chain (RR-40: Entity Framework
/// wraps the provider's exception in a DbUpdateException): the SQLSTATEs that restart the whole command (D-41), and
/// the one failure that is a command's own rejection rather than an error: a unique violation of a mapped business
/// key (RR-45). A numeric overflow is no longer mapped here: the posting service turns a 22003 from its own writes
/// into its rejection, and an overflow anywhere else stays a 500 (RR-83).
/// </summary>
internal static class DatabaseErrors
{
    public const string SerializationFailure = "40001";
    public const string DeadlockDetected = "40P01";
    public const string UniqueViolation = "23505";

    /// <summary>The PostgreSQL exception inside the chain, or null when the failure is not one.</summary>
    public static PostgresException? FindPostgres(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres;
            }

            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (FindPostgres(inner) is { } found)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// D-41: only a serialization failure or a deadlock restarts the command, also when wrapped (RR-40). The SQLSTATE is
    /// read from the first provider exception in the chain, so a fixture's synthetic DbException counts like Npgsql's.
    /// </summary>
    public static bool IsRetryable(Exception exception) => FindSqlState(exception) is SerializationFailure or DeadlockDetected;

    /// <summary>The SQLSTATE of the first provider exception in the chain, or null when there is none.</summary>
    public static string? FindSqlState(Exception? exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } state })
            {
                return state;
            }

            if (current is AggregateException aggregate)
            {
                foreach (Exception inner in aggregate.InnerExceptions)
                {
                    if (FindSqlState(inner) is { } found)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The rejection a database failure inside the handler stands for, or null when it is an error: a 23505 on a
    /// registered business-key constraint becomes that rule's error (RR-45). Every other state is an error: a 22003
    /// (RR-83), the trigger backstops (RR-47) and the D-60 immutability triggers' 23514 are never mapped.
    /// </summary>
    public static (PostgresException Exception, Error Error)? MapRejection(Exception exception, IEnumerable<UniqueConstraintRule> uniqueRules)
    {
        if (FindPostgres(exception) is not { } postgres)
        {
            return null;
        }

        if (postgres.SqlState == UniqueViolation && postgres.ConstraintName is { } constraint)
        {
            UniqueConstraintRule? rule = uniqueRules.FirstOrDefault(candidate => string.Equals(candidate.ConstraintName, constraint, StringComparison.Ordinal));
            if (rule is null)
            {
                return null;
            }

            return (postgres, rule.Kind == ErrorKind.PreconditionFailed ? Error.PreconditionFailed(rule.Code, rule.Message, rule.RuleId) : Error.Rule(rule.RuleId, rule.Code, rule.Message));
        }

        return null;
    }
}

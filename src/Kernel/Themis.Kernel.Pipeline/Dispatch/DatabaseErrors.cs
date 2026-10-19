using System.Data.Common;
using Npgsql;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Dispatch;

/// <summary>
/// What the pipeline reads out of a database failure, wherever it sits in the exception chain (RR-40: Entity Framework
/// wraps the provider's exception in a DbUpdateException): the SQLSTATEs that restart the whole command (D-41), and
/// the two failures that are a command's own rejection rather than an error: a unique violation of a mapped business
/// key (RR-45) and a numeric overflow found while posting (RR-46, under R-DOC-01 with its own code).
/// </summary>
internal static class DatabaseErrors
{
    public const string SerializationFailure = "40001";
    public const string DeadlockDetected = "40P01";
    public const string UniqueViolation = "23505";
    public const string NumericValueOutOfRange = "22003";
    public const string AmountOutOfRangeCode = "amount_out_of_range";

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
    /// registered business-key constraint becomes that rule's error (RR-45); a 22003 becomes amount_out_of_range under
    /// R-DOC-01 (RR-46, Specification shared rules for guards without their own rule). Trigger backstops raise other
    /// states and are never mapped (RR-47).
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
            return rule is null ? null : (postgres, Error.Rule(rule.RuleId, rule.Code, rule.Message));
        }

        if (postgres.SqlState == NumericValueOutOfRange)
        {
            return (postgres, Error.Rule("R-DOC-01", AmountOutOfRangeCode, "An amount computed by the command exceeds the range of the column that stores it."));
        }

        return null;
    }
}

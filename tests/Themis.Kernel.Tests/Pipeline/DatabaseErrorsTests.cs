using Microsoft.EntityFrameworkCore;
using Npgsql;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Dispatch;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Pipeline;

/// <summary>The pipeline's reading of database failures: retry on wrapped errors (RR-40, D-41), mapped rejections (RR-45, RR-46).</summary>
[Trait("Category", "Unit")]
public sealed class DatabaseErrorsTests
{
    private static readonly UniqueConstraintRule ItemNumber = new("ux_item_item_number", "R-MD-05", "item_number_duplicate", "The item number already exists.");

    private static PostgresException Postgres(string sqlState, string? constraint = null) =>
        new("probe", "ERROR", "ERROR", sqlState, constraintName: constraint);

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public void RR_40_a_retryable_SQLSTATE_wrapped_by_Entity_Framework_in_a_DbUpdateException_is_retried(string sqlState)
    {
        DbUpdateException wrapped = new("SaveChanges failed", Postgres(sqlState));

        Assert.True(DatabaseErrors.IsRetryable(wrapped));
        Assert.True(DatabaseErrors.IsRetryable(Postgres(sqlState)));
        Assert.True(DatabaseErrors.IsRetryable(new InvalidOperationException("outer", new DbUpdateException("middle", Postgres(sqlState)))));
        Assert.True(DatabaseErrors.IsRetryable(new AggregateException(new DbUpdateException("in aggregate", Postgres(sqlState)))));
    }

    [Theory]
    [InlineData("23505")]
    [InlineData("22003")]
    [InlineData("23514")]
    [InlineData("42501")]
    public void D_41_no_other_SQLSTATE_and_no_exception_without_one_is_retried(string sqlState)
    {
        Assert.False(DatabaseErrors.IsRetryable(new DbUpdateException("SaveChanges failed", Postgres(sqlState))));
        Assert.False(DatabaseErrors.IsRetryable(new InvalidOperationException("no database error")));
        Assert.False(DatabaseErrors.IsRetryable(new DbUpdateException("no inner exception")));
    }

    [Fact]
    public void RR_45_a_unique_violation_of_a_registered_constraint_maps_to_its_rule_error_and_an_unmapped_one_stays_an_error()
    {
        (PostgresException Exception, Error Error)? mapped = DatabaseErrors.MapRejection(new DbUpdateException("wrapped", Postgres("23505", "ux_item_item_number")), [ItemNumber]);

        Assert.NotNull(mapped);
        Assert.Equal(("R-MD-05", "item_number_duplicate", ErrorKind.RuleViolation), (mapped.Value.Error.RuleId, mapped.Value.Error.Code, mapped.Value.Error.Kind));
        Assert.Null(DatabaseErrors.MapRejection(Postgres("23505", "ux_other"), [ItemNumber]));
        Assert.Null(DatabaseErrors.MapRejection(Postgres("23505"), [ItemNumber]));
        Assert.Null(DatabaseErrors.MapRejection(Postgres("23505", "ux_item_item_number"), []));
    }

    [Fact]
    public void RR_83_a_numeric_overflow_and_every_trigger_backstop_stay_errors_and_are_never_mapped()
    {
        // RR-83: the dispatcher no longer maps 22003; the posting service turns its own overflow into its rejection.
        Assert.Null(DatabaseErrors.MapRejection(Postgres("22003"), [ItemNumber]));
        Assert.Null(DatabaseErrors.MapRejection(new DbUpdateException("wrapped", Postgres("22003")), [ItemNumber]));
        // RR-47: the deferred triggers raise 23514 with their own constraint name; never retried, never mapped.
        Assert.Null(DatabaseErrors.MapRejection(Postgres("23514", "ct_journal_entry_balanced"), [ItemNumber]));
        // D-60: the immutability triggers raise 23514 too; never retried, never mapped.
        Assert.Null(DatabaseErrors.MapRejection(Postgres("23514", "ct_journal_entry_immutable"), [ItemNumber]));
        Assert.False(DatabaseErrors.IsRetryable(Postgres("23514", "ct_audit_event_immutable")));
        Assert.Null(DatabaseErrors.MapRejection(new InvalidOperationException("not a database error"), [ItemNumber]));
    }
}

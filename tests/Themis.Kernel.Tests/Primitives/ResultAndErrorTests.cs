using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Primitives;

/// <summary>Errors carry a stable code and the rule ID that failed (R-API-03); event names follow D-44.</summary>
[Trait("Category", "Unit")]
public sealed class ResultAndErrorTests
{
    [Fact]
    public void R_API_03_an_error_carries_a_stable_code_and_the_rule_id()
    {
        Error error = Error.Rule("R-LED-04", "period_closed", "Period 2026-01 is closed.");

        Assert.Equal("period_closed", error.Code);
        Assert.Equal("R-LED-04", error.RuleId);
        Assert.Equal(ErrorKind.RuleViolation, error.Kind);
        Assert.Equal("period_closed (R-LED-04): Period 2026-01 is closed.", error.ToString());
    }

    [Theory]
    [InlineData("PeriodClosed")]
    [InlineData("period closed")]
    [InlineData("")]
    public void Error_codes_outside_snake_case_are_refused(string code)
    {
        Assert.Throws<ArgumentException>(() => Error.Validation(code, "message"));
    }

    [Theory]
    [InlineData("R-LED-4")]
    [InlineData("RLED04")]
    [InlineData("X-01")]
    public void Rule_ids_outside_the_design_format_are_refused(string ruleId)
    {
        Assert.Throws<ArgumentException>(() => Error.Rule(ruleId, "code", "message"));
    }

    [Fact]
    public void R_SEC_01_and_R_API_02_factories_default_to_their_rule_ids()
    {
        Assert.Equal("R-SEC-01", Error.Forbidden("permission_missing", "no").RuleId);
        Assert.Equal("R-API-02", Error.PreconditionFailed("version_stale", "no").RuleId);
    }

    [Fact]
    public void A_result_converts_implicitly_from_a_value_and_from_an_error()
    {
        Result<int> success = 42;
        Result<int> failure = Error.NotFound("item_not_found", "no such item");

        Assert.True(success.IsSuccess);
        Assert.Equal(42, success.Value);
        Assert.True(failure.IsFailure);
        Assert.Throws<InvalidOperationException>(() => failure.Value);
        Assert.Throws<ArgumentException>(() => Result.Failure([]));
    }

    [Theory]
    [InlineData("ITEM", "CREATE", "ITEM_CREATED")]
    [InlineData("INVENTORY_ADJUSTMENT", "DISCARD", "INVENTORY_ADJUSTMENT_DISCARDED")]
    [InlineData("INVENTORY_ADJUSTMENT", "POST", "INVENTORY_ADJUSTMENT_POSTED")]
    [InlineData("FIXTURE_COUNTER", "TOUCH", "FIXTURE_COUNTER_TOUCHED")]
    [InlineData("SALES_ORDER", "MARK_COMPLETE", "SALES_ORDER_MARK_COMPLETED")]
    public void D_44_event_names_are_the_type_plus_the_command_in_past_tense(string type, string verb, string expected)
    {
        Assert.Equal(expected, EventNames.Generated(type, verb));
        Assert.Equal(1, EventNames.DefaultSchemaVersion);
    }

    [Fact]
    public void D_44_event_names_refuse_codes_outside_upper_snake_case()
    {
        Assert.Throws<ArgumentException>(() => EventNames.Generated("item", "create"));
    }
}

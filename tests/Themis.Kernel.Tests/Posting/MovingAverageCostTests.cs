using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Posting;

/// <summary>
/// The moving average algorithm of the Phase 0 Specification on its own (R-INV-05, R-INV-07, R-INV-09, R-MON-02):
/// the 13 reference calculations of the Build Brief step by step, the rounding points, and the rejection of an
/// inbound line without an entered cost on an item without stock or average.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MovingAverageCostTests
{
    private const int Minor = 2;

    [Fact]
    public void R_INV_05_TEST_001_two_receipts_at_different_costs_average_and_the_outbound_lines_cost_at_the_average_until_the_stock_empties()
    {
        Costing step1 = MovingAverageCost.Inbound(CostState.Empty, 100m, 2.00m, Minor).Value;
        Costing step2 = MovingAverageCost.Inbound(step1.Next, 100m, 3.00m, Minor).Value;
        Costing step3 = MovingAverageCost.Outbound(step2.Next, 50m, Minor);
        Costing step4 = MovingAverageCost.Outbound(step3.Next, 150m, Minor);

        Assert.Equal((200.00m, 100m, 200.00m, 2.00m), (step1.CostAmount, step1.Next.Quantity, step1.Next.Value, step1.Next.AverageCost));
        Assert.Equal((300.00m, 200m, 500.00m, 2.50m), (step2.CostAmount, step2.Next.Quantity, step2.Next.Value, step2.Next.AverageCost));
        Assert.Equal((-125.00m, 150m, 375.00m, 2.50m), (step3.CostAmount, step3.Next.Quantity, step3.Next.Value, step3.Next.AverageCost));
        // R-INV-07: emptying the stock takes the full remaining value; the average is kept for the next receipt without cost.
        Assert.Equal((-375.00m, 0m, 0m, 2.50m), (step4.CostAmount, step4.Next.Quantity, step4.Next.Value, step4.Next.AverageCost));
        // Step 5 is R-INV-03, the stock check, which the posting service runs before the algorithm; the algorithm itself refuses more than the stock.
        Assert.Throws<ArgumentOutOfRangeException>(() => MovingAverageCost.Outbound(step4.Next, 1m, Minor));
    }

    [Fact]
    public void R_MON_02_TEST_002_the_outbound_cost_is_rounded_half_away_from_zero_once_from_the_exact_value_over_quantity()
    {
        Costing step6 = MovingAverageCost.Inbound(CostState.Empty, 3m, 1.00m, Minor).Value;
        Costing step7 = MovingAverageCost.Inbound(step6.Next, 3m, 1.01m, Minor).Value;
        Costing step8 = MovingAverageCost.Outbound(step7.Next, 1m, Minor);
        Costing step9 = MovingAverageCost.Outbound(step8.Next, 5m, Minor);

        Assert.Equal((3.00m, 3m, 3.00m, 1.00m), (step6.CostAmount, step6.Next.Quantity, step6.Next.Value, step6.Next.AverageCost));
        Assert.Equal((3.03m, 6m, 6.03m, 1.005m), (step7.CostAmount, step7.Next.Quantity, step7.Next.Value, step7.Next.AverageCost));
        // 1 x 6.03 / 6 = 1.005, rounded half away from zero to 1.01.
        Assert.Equal((-1.01m, 5m, 5.02m, 1.004m), (step8.CostAmount, step8.Next.Quantity, step8.Next.Value, step8.Next.AverageCost));
        Assert.Equal((-5.02m, 0m, 0m, 1.004m), (step9.CostAmount, step9.Next.Quantity, step9.Next.Value, step9.Next.AverageCost));
    }

    [Fact]
    public void RR_74_half_way_amounts_round_in_the_companys_mode_HalfEven_to_the_even_cent_HalfAwayFromZero_away_and_the_default_is_HalfAwayFromZero()
    {
        // Inbound: 1 x 2.125 and 1 x 2.135 (R-MON-02 at the company's minor units).
        Costing awayLow = MovingAverageCost.Inbound(CostState.Empty, 1m, 2.125m, Minor, RoundingMode.HalfAwayFromZero).Value;
        Costing evenLow = MovingAverageCost.Inbound(CostState.Empty, 1m, 2.125m, Minor, RoundingMode.HalfEven).Value;
        Costing evenHigh = MovingAverageCost.Inbound(CostState.Empty, 1m, 2.135m, Minor, RoundingMode.HalfEven).Value;
        Costing implicitDefault = MovingAverageCost.Inbound(CostState.Empty, 1m, 2.125m, Minor).Value;
        Assert.Equal((2.13m, 2.13m), (awayLow.CostAmount, awayLow.Next.Value));
        Assert.Equal((2.12m, 2.12m), (evenLow.CostAmount, evenLow.Next.Value));
        Assert.Equal((2.14m, 2.14m), (evenHigh.CostAmount, evenHigh.Next.Value));
        Assert.Equal(awayLow, implicitDefault);

        // Outbound: TEST-002's step 8 (1 x 6.03 / 6 = 1.005) rounds to 1.01 away from zero and to 1.00 half even; the stock value follows.
        Costing stock = MovingAverageCost.Inbound(MovingAverageCost.Inbound(CostState.Empty, 3m, 1.00m, Minor).Value.Next, 3m, 1.01m, Minor).Value;
        Costing away = MovingAverageCost.Outbound(stock.Next, 1m, Minor, RoundingMode.HalfAwayFromZero);
        Costing even = MovingAverageCost.Outbound(stock.Next, 1m, Minor, RoundingMode.HalfEven);
        Assert.Equal((-1.01m, 5m, 5.02m), (away.CostAmount, away.Next.Quantity, away.Next.Value));
        Assert.Equal((-1.00m, 5m, 5.03m), (even.CostAmount, even.Next.Quantity, even.Next.Value));
    }

    [Fact]
    public void R_INV_05_TEST_003_value_over_quantity_is_used_while_stock_is_above_0_never_the_stored_average()
    {
        Costing step10 = MovingAverageCost.Inbound(CostState.Empty, 100_000_000m, 0.01m, Minor).Value;
        Costing step11 = MovingAverageCost.Inbound(step10.Next, 200_000_000m, 0.02m, Minor).Value;
        Costing step12 = MovingAverageCost.Outbound(step11.Next, 150_000_000m, Minor);
        Costing step13 = MovingAverageCost.Inbound(step12.Next, 150_000_000m, null, Minor).Value;

        Assert.Equal((1_000_000.00m, 100_000_000m, 1_000_000.00m, 0.01m), (step10.CostAmount, step10.Next.Quantity, step10.Next.Value, step10.Next.AverageCost));
        Assert.Equal((4_000_000.00m, 300_000_000m, 5_000_000.00m, 0.0166666667m), (step11.CostAmount, step11.Next.Quantity, step11.Next.Value, step11.Next.AverageCost));
        // The stored average (0.0166666667) would give 2,500,000.01; value / quantity gives 2,500,000.00 exactly.
        Assert.Equal((-2_500_000.00m, 150_000_000m, 2_500_000.00m, 0.0166666667m), (step12.CostAmount, step12.Next.Quantity, step12.Next.Value, step12.Next.AverageCost));
        Assert.Equal((2_500_000.00m, 300_000_000m, 5_000_000.00m, 0.0166666667m), (step13.CostAmount, step13.Next.Quantity, step13.Next.Value, step13.Next.AverageCost));
    }

    [Fact]
    public void R_INV_05_an_inbound_line_without_an_entered_cost_takes_the_stored_average_only_at_zero_stock_and_is_rejected_without_either()
    {
        Result<Costing> nothing = MovingAverageCost.Inbound(CostState.Empty, 5m, null, Minor);
        Result<Costing> atAverage = MovingAverageCost.Inbound(new CostState(0m, 0m, 2.50m), 4m, null, Minor);

        Assert.True(nothing.IsFailure);
        Assert.Equal("R-INV-05", nothing.Primary!.RuleId);
        Assert.Equal(MovingAverageCost.EnteredCostRequired, nothing.Primary.Code);
        Assert.True(atAverage.IsSuccess);
        Assert.Equal(10.00m, atAverage.Value.CostAmount);
        Assert.Equal(new CostState(4m, 10.00m, 2.5m), atAverage.Value.Next);
    }

    [Fact]
    public void R_MON_02_costs_round_to_the_currency_minor_unit_the_average_to_10_decimals_and_the_unit_cost_is_cost_over_base_quantity()
    {
        Costing receipt = MovingAverageCost.Inbound(CostState.Empty, 3m, 0.333m, 0).Value;
        Costing fine = MovingAverageCost.Inbound(CostState.Empty, 3m, 1.00m, Minor).Value;

        // A currency without minor units (minor units 0) rounds 0.999 to 1.
        Assert.Equal(1m, receipt.CostAmount);
        Assert.Equal(0.3333333333m, receipt.Next.AverageCost);
        Assert.Equal(1.00m, fine.Next.AverageCost);
        Assert.Equal(2.5m, MovingAverageCost.UnitCost(-125.00m, -50m));
        Assert.Equal(1.01m, MovingAverageCost.UnitCost(-1.01m, -1m));
        Assert.Equal(0.3333333333m, MovingAverageCost.UnitCost(1m, 3m));
        Assert.Equal(MovingAverageCost.AverageDecimals, DecimalRounding.Scale(MovingAverageCost.UnitCost(1m, 3m)));
    }
}

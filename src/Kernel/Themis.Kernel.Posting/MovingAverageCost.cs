using Themis.Kernel.Primitives;

namespace Themis.Kernel.Posting;

/// <summary>The cost state of a company's item as the algorithm carries it from line to line: quantity on hand, inventory value and the informational average (Specification, Caches).</summary>
internal sealed record CostState(decimal Quantity, decimal Value, decimal? AverageCost)
{
    public static CostState Empty { get; } = new(0m, 0m, null);
}

/// <summary>What one movement did to the cost state: the signed cost amount of the line and the state after it.</summary>
internal sealed record Costing(decimal CostAmount, CostState Next);

/// <summary>
/// The moving average cost algorithm of the Phase 0 Specification (R-INV-05), per company and item, in recording
/// order (R-INV-09): inbound with an entered cost (step 1), inbound without one (step 2: value / quantity while
/// stock is above 0, the stored average only at zero stock, rejected without either), outbound (step 3: the full
/// remaining value when the movement empties the stock, R-INV-07, else q x value / quantity as one exact decimal
/// expression), and the average after every movement (step 6: value / quantity rounded half away from zero to 10
/// decimals, kept at zero stock). Costs are rounded to the currency's minor unit at these points only, in the
/// company's rounding mode (R-MON-02, RR-74); the informational average and unit cost keep the Specification's rule,
/// half away from zero at 10 decimals.
/// </summary>
internal static class MovingAverageCost
{
    public const int AverageDecimals = 10;
    public const string EnteredCostRequired = "entered_unit_cost_required";

    /// <summary>Step 1 or 2: an inbound movement of a positive quantity.</summary>
    public static Result<Costing> Inbound(CostState state, decimal quantity, decimal? enteredUnitCost, int minorUnits, RoundingMode mode)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        decimal cost;
        if (enteredUnitCost is { } unitCost)
        {
            // Step 1: cost = q x entered unit cost, rounded to the currency's minor unit.
            cost = DecimalRounding.Round(quantity * unitCost, minorUnits, mode);
        }
        else if (state.Quantity > 0)
        {
            // Step 2: while stock is above 0, q x value / quantity, computed exactly and then rounded; never the stored average.
            cost = DecimalRounding.Round(quantity * state.Value / state.Quantity, minorUnits, mode);
        }
        else if (state.AverageCost is { } average)
        {
            // Step 2 at zero stock: the stored average is the fallback.
            cost = DecimalRounding.Round(quantity * average, minorUnits, mode);
        }
        else
        {
            // R-INV-05: an inbound line without an entered cost needs stock or a stored average.
            return Error.Rule("R-INV-05", EnteredCostRequired, "An inbound line without an entered unit cost needs stock on hand or a stored average cost.");
        }

        return new Costing(cost, After(state.Quantity + quantity, state.Value + cost, state.AverageCost));
    }

    /// <summary>Step 3: an outbound movement of a positive magnitude; the caller has checked the stock (R-INV-03). The cost amount is returned negative, signed like the quantity.</summary>
    public static Costing Outbound(CostState state, decimal quantity, int minorUnits, RoundingMode mode)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quantity, state.Quantity);
        // R-INV-07: emptying the stock takes the full remaining value, so stock value never turns negative.
        decimal cost = quantity == state.Quantity ? state.Value : DecimalRounding.Round(quantity * state.Value / state.Quantity, minorUnits, mode);
        return new Costing(-cost, After(state.Quantity - quantity, state.Value - cost, state.AverageCost));
    }

    /// <summary>Step 6: the average after a movement; at zero stock the value is 0 (R-INV-07) and the last average is kept for step 2.</summary>
    public static CostState After(decimal quantity, decimal value, decimal? previousAverage) =>
        quantity > 0
            ? new CostState(quantity, value, DecimalRounding.Round(value / quantity, AverageDecimals))
            : new CostState(0m, 0m, previousAverage);

    /// <summary>The unit cost stored on the line and its entry (R-DOC-05): cost amount / base quantity, rounded to 10 decimals.</summary>
    public static decimal UnitCost(decimal costAmount, decimal quantityBase) => DecimalRounding.Round(costAmount / quantityBase, AverageDecimals);
}

namespace Themis.Kernel.Primitives;

/// <summary>Exact decimal rounding at the defined points (R-MON-02). Never used for intermediate results.</summary>
public static class DecimalRounding
{
    public static decimal Round(decimal value, int decimals, RoundingMode mode = RoundingMode.HalfAwayFromZero)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 28);

        return mode switch
        {
            RoundingMode.HalfAwayFromZero => Math.Round(value, decimals, MidpointRounding.AwayFromZero),
            RoundingMode.HalfEven => Math.Round(value, decimals, MidpointRounding.ToEven),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown rounding mode."),
        };
    }

    /// <summary>The number of decimal places a value carries, as stored in its scale.</summary>
    public static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    /// <summary>The same value without trailing zeros: 0.125000 reads back as 0.125 (D-37).</summary>
    public static decimal Trim(decimal value) => value / 1.0000000000000000000000000000m;

    /// <summary>
    /// The same value at exactly the unit's decimals, as a quantity is shown in its unit (R-INV-02): 10 pieces at 0
    /// decimals is 10, 0.25 kilograms at 3 decimals is 0.250. A value finer than the unit keeps its own scale; the
    /// caller has already refused it. Never rounds.
    /// </summary>
    public static decimal AtScale(decimal value, int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 28);
        return Trim(value) + new decimal(0, 0, 0, false, (byte)decimals);
    }
}

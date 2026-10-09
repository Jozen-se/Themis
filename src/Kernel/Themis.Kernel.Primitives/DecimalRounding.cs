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
}

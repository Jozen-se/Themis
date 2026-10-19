using System.Numerics;

namespace Themis.Kernel.Primitives;

/// <summary>
/// Magnitude checks against the PostgreSQL numeric columns that store money, quantities and costs (RR-46; Specification,
/// Data types): a value fits numeric(precision, scale) when its integer part has at most precision minus scale digits.
/// The product check compares exact integers built from the decimals' mantissas, so no product is ever computed in
/// C# decimal, which could overflow before the comparison (R-MON-01: no floating point anywhere).
/// </summary>
public static class DecimalBounds
{
    /// <summary>True when |value| is below 10^(precision - scale), the largest magnitude numeric(precision, scale) holds.</summary>
    public static bool FitsNumeric(decimal value, int precision, int scale)
    {
        (BigInteger mantissa, int valueScale) = Decompose(value);
        return mantissa < BigInteger.Pow(10, precision - scale + valueScale);
    }

    /// <summary>True when |left x right| is below 10^(precision - scale), computed exactly from the two mantissas.</summary>
    public static bool ProductFitsNumeric(decimal left, decimal right, int precision, int scale)
    {
        (BigInteger leftMantissa, int leftScale) = Decompose(left);
        (BigInteger rightMantissa, int rightScale) = Decompose(right);
        return leftMantissa * rightMantissa < BigInteger.Pow(10, precision - scale + leftScale + rightScale);
    }

    /// <summary>The absolute mantissa and the scale of a decimal: |value| = mantissa / 10^scale.</summary>
    private static (BigInteger Mantissa, int Scale) Decompose(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger mantissa = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        return (mantissa, (bits[3] >> 16) & 0xFF);
    }
}

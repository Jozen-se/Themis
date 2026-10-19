using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Primitives;

/// <summary>RR-46: magnitude checks against the numeric columns, exact and without computing a product in decimal (R-MON-01).</summary>
[Trait("Category", "Unit")]
public sealed class DecimalBoundsTests
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("99999999999999.999999", true)]
    [InlineData("-99999999999999.999999", true)]
    [InlineData("100000000000000", false)]
    [InlineData("-100000000000000", false)]
    [InlineData("100000000000000.000001", false)]
    public void RR_46_a_value_fits_numeric_20_6_when_its_integer_part_has_at_most_14_digits(string text, bool fits)
    {
        Assert.Equal(fits, DecimalBounds.FitsNumeric(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), 20, 6));
    }

    [Theory]
    [InlineData("999999999999999999.9999999999", true)]
    [InlineData("1000000000000000000", false)]
    public void RR_46_a_unit_cost_fits_numeric_28_10_when_its_integer_part_has_at_most_18_digits(string text, bool fits)
    {
        Assert.Equal(fits, DecimalBounds.FitsNumeric(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture), 28, 10));
    }

    [Theory]
    [InlineData("99999999999999", "100", true)]
    [InlineData("99999999999999", "100.00001", false)]
    [InlineData("10000000000000", "1000", false)]
    [InlineData("9999999999999.5", "1000", true)]
    [InlineData("-99999999999999", "100", true)]
    [InlineData("99999999999999", "999999999999999999", false)]
    [InlineData("0.5", "0.000000001", true)]
    public void RR_46_a_product_fits_numeric_20_4_when_it_is_below_10_to_the_16_computed_exactly_without_overflow(string left, string right, bool fits)
    {
        decimal a = decimal.Parse(left, System.Globalization.CultureInfo.InvariantCulture);
        decimal b = decimal.Parse(right, System.Globalization.CultureInfo.InvariantCulture);

        // 99,999,999,999,999 x 999,999,999,999,999,999 overflows System.Decimal; the check must answer anyway (R-MON-01: no floating point).
        Assert.Equal(fits, DecimalBounds.ProductFitsNumeric(a, b, 20, 4));
        Assert.Equal(fits, DecimalBounds.ProductFitsNumeric(b, a, 20, 4));
    }
}

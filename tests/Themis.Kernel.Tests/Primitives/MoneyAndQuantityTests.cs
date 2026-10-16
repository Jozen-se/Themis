using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Primitives;

/// <summary>Money and Quantity are exact decimals with a currency or unit and never mix them (R-MON-01, R-INV-02).</summary>
[Trait("Category", "Unit")]
public sealed class MoneyAndQuantityTests
{
    private static readonly CurrencyCode Sek = CurrencyCode.From("SEK");
    private static readonly CurrencyCode Eur = CurrencyCode.From("EUR");
    private static readonly UnitCode Pcs = UnitCode.From("PCS");

    [Fact]
    public void R_MON_01_money_arithmetic_keeps_the_decimal_scale()
    {
        Money total = new Money(2_500_000.00m, Sek) + new Money(0.01m, Sek);

        Assert.Equal(2_500_000.01m, total.Amount);
        Assert.Equal(2, DecimalRounding.Scale(total.Amount));
        Assert.Equal("2500000.01 SEK", total.ToString());
    }

    [Fact]
    public void R_MON_01_money_in_different_currencies_cannot_be_added()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => new Money(1m, Sek) + new Money(1m, Eur));

        Assert.Contains("R-MON-01", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2.345", 2, "2.35")]
    [InlineData("-2.345", 2, "-2.35")]
    [InlineData("2.5", 0, "3")]
    [InlineData("0.0166666667", 4, "0.0167")]
    public void Money_rounds_half_away_from_zero_by_default(string amount, int minorUnits, string expected)
    {
        Money rounded = new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Sek).Round(minorUnits);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rounded.Amount);
    }

    [Theory]
    [InlineData("2.5", "2")]
    [InlineData("3.5", "4")]
    [InlineData("-2.5", "-2")]
    public void Money_rounds_half_even_when_asked(string amount, string expected)
    {
        Money rounded = new Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Sek).Round(0, RoundingMode.HalfEven);

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), rounded.Amount);
    }

    [Fact]
    public void R_INV_02_a_quantity_with_more_decimals_than_the_unit_allows_is_rejected()
    {
        Result<Quantity> result = Quantity.Create(1.5m, Pcs, unitDecimals: 0);

        Assert.True(result.IsFailure);
        Assert.Equal("quantity_precision", result.Primary!.Code);
        Assert.Equal("R-INV-02", result.Primary.RuleId);
    }

    [Fact]
    public void R_INV_02_a_quantity_within_the_unit_precision_is_created()
    {
        Result<Quantity> result = Quantity.Create(150_000_000m, Pcs, unitDecimals: 0);

        Assert.True(result.IsSuccess);
        Assert.Equal(150_000_000m, result.Value.Value);
        Assert.Equal(Pcs, result.Value.Unit);
    }

    [Fact]
    public void Quantities_in_different_units_cannot_be_added()
    {
        Assert.Throws<InvalidOperationException>(() => new Quantity(1m, Pcs) + new Quantity(1m, UnitCode.From("KG")));
    }

    [Fact]
    public void Codes_are_upper_case_and_bounded()
    {
        Assert.Equal("ITEM_01", Code.From("item_01").Value);
        Assert.False(Code.TryFrom(new string('A', Code.MaxLength + 1), out _));
        Assert.False(Code.TryFrom("item 01", out _));
        Assert.False(CurrencyCode.TryFrom("SE", out _));
    }

    [Fact]
    public void A_date_range_is_inclusive_from_and_exclusive_to()
    {
        DateRange range = new(BusinessDate.Parse("2026-01-01"), BusinessDate.Parse("2026-02-01"));

        Assert.True(range.Contains(BusinessDate.Parse("2026-01-01")));
        Assert.True(range.Contains(BusinessDate.Parse("2026-01-31")));
        Assert.False(range.Contains(BusinessDate.Parse("2026-02-01")));
        Assert.True(new DateRange(BusinessDate.Parse("2026-01-15"), null).Overlaps(range));
        Assert.False(new DateRange(BusinessDate.Parse("2026-02-01"), null).Overlaps(range));
    }

    [Theory]
    [InlineData("0.125000", "0.125")]
    [InlineData("10.000000", "10")]
    [InlineData("100", "100")]
    [InlineData("0", "0")]
    [InlineData("-1.50", "-1.5")]
    public void D_37_Trim_removes_trailing_zeros_and_nothing_else(string value, string expected)
    {
        Assert.Equal(expected, DecimalRounding.Trim(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("10.000000", 0, "10")]
    [InlineData("0.25", 3, "0.250")]
    [InlineData("2.5000", 3, "2.500")]
    [InlineData("7", 2, "7.00")]
    [InlineData("0.1234", 3, "0.1234")]
    public void R_INV_02_AtScale_shows_a_quantity_at_the_units_decimals_and_never_rounds(string value, int decimals, string expected)
    {
        Assert.Equal(expected, DecimalRounding.AtScale(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), decimals).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}

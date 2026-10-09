using System.Globalization;
using System.Text.Json;
using CsCheck;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Invariants.Tests;

/// <summary>
/// Properties of the primitives that must hold for every input (R-MON-01, R-INV-02, D-37, D-44). CsCheck shrinks a
/// failing case to the smallest one; the seed is printed with the failure so a run can be repeated.
/// </summary>
[Trait("Category", "Invariant")]
public sealed class PrimitiveInvariantTests
{
    private static readonly CurrencyCode Sek = CurrencyCode.From("SEK");
    private static readonly UnitCode Pcs = UnitCode.From("PCS");

    /// <summary>Decimals with up to 28 significant digits and a scale of 0 to 10, as money and quantities use them.</summary>
    private static readonly Gen<decimal> Amounts =
        Gen.Select(Gen.Long[-999_999_999_999_999L, 999_999_999_999_999L], Gen.Int[0, 10], (mantissa, scale) => new decimal(mantissa) / Pow10(scale));

    private static readonly Gen<string> UpperCodes =
        Gen.Select(Gen.Char['A', 'Z'], Gen.String[Gen.Char["ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_"], 0, 20], (first, rest) => first + rest);

    [Fact]
    public void R_MON_01_money_addition_is_exact_and_commutative()
    {
        Gen.Select(Amounts, Amounts).Sample((a, b) =>
        {
            Money left = new(a, Sek);
            Money right = new(b, Sek);
            return (left + right).Amount == a + b && left + right == right + left && (left + right - right).Amount == a;
        });
    }

    [Fact]
    public void R_MON_01_rounding_never_changes_the_value_by_more_than_half_a_minor_unit_and_is_idempotent()
    {
        Gen.Select(Amounts, Gen.Int[0, 6]).Sample((amount, minorUnits) =>
        {
            Money rounded = new Money(amount, Sek).Round(minorUnits);
            decimal step = 1m / Pow10(minorUnits);
            return Math.Abs(rounded.Amount - amount) <= step / 2m
                && DecimalRounding.Scale(rounded.Amount) <= minorUnits
                && rounded.Round(minorUnits) == rounded;
        });
    }

    [Fact]
    public void R_MON_01_half_away_from_zero_and_half_even_differ_only_on_exact_halves()
    {
        Gen.Select(Amounts, Gen.Int[0, 6]).Sample((amount, decimals) =>
        {
            decimal away = DecimalRounding.Round(amount, decimals, RoundingMode.HalfAwayFromZero);
            decimal even = DecimalRounding.Round(amount, decimals, RoundingMode.HalfEven);
            decimal remainder = Math.Abs(amount * Pow10(decimals)) % 1m;
            return remainder == 0.5m || away == even;
        });
    }

    [Fact]
    public void R_INV_02_a_quantity_is_created_exactly_when_its_scale_fits_the_unit()
    {
        Gen.Select(Amounts, Gen.Int[0, 6]).Sample((value, unitDecimals) =>
        {
            Result<Quantity> result = Quantity.Create(value, Pcs, unitDecimals);
            bool fits = DecimalRounding.Scale(value.Normalize()) <= unitDecimals;
            return result.IsSuccess == fits && (!fits || result.Value.Value == value);
        });
    }

    [Fact]
    public void D_37_every_decimal_survives_the_JSON_round_trip_as_a_string_with_its_scale()
    {
        Amounts.Sample(amount =>
        {
            string json = JsonSerializer.Serialize(amount, ThemisJson.Options);
            decimal back = JsonSerializer.Deserialize<decimal>(json, ThemisJson.Options);
            return json == "\"" + amount.ToString(CultureInfo.InvariantCulture) + "\""
                && back == amount
                && DecimalRounding.Scale(back) == DecimalRounding.Scale(amount);
        });
    }

    [Fact]
    public void D_37_a_decimal_sent_as_a_JSON_number_reads_the_same_as_the_string_form()
    {
        Amounts.Sample(amount =>
        {
            string number = amount.ToString(CultureInfo.InvariantCulture);
            return JsonSerializer.Deserialize<decimal>(number, ThemisJson.Options) == JsonSerializer.Deserialize<decimal>("\"" + number + "\"", ThemisJson.Options);
        });
    }

    [Fact]
    public void D_44_generated_event_names_are_upper_snake_case_and_end_in_ED_or_D()
    {
        Gen.Select(UpperCodes, UpperCodes).Sample((type, verb) =>
        {
            string name = EventNames.Generated(type, verb);
            return name.StartsWith(type + "_", StringComparison.Ordinal)
                && name.EndsWith("ED", StringComparison.Ordinal)
                && name.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')
                && name.Length == type.Length + 1 + verb.Length + (verb.EndsWith('E') ? 1 : 2);
        });
    }

    [Fact]
    public void A_result_is_either_a_success_with_a_value_or_a_failure_with_errors()
    {
        Gen.Select(Gen.Int, Gen.Bool).Sample((value, succeed) =>
        {
            Result<int> result = succeed ? Result<int>.Success(value) : Result<int>.Failure(Error.Validation("value_rejected", "rejected"));
            return result.IsSuccess != result.IsFailure
                && (result.IsSuccess ? result.Value == value && result.Errors.Count == 0 : result.Errors.Count == 1 && result.Primary is not null);
        });
    }

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}

internal static class DecimalExtensions
{
    /// <summary>Strips trailing zeros so that 1.50 and 1.5 report the same scale.</summary>
    public static decimal Normalize(this decimal value) => value / 1.000000000000000000000000000000000m;
}

using System.Globalization;

namespace Themis.Kernel.Primitives;

/// <summary>
/// An exact decimal quantity with its unit (R-MON-01). Arithmetic across units is refused; conversions happen once,
/// at the edge (R-INV-02). The unit's permitted decimals are data on the unit, so a quantity entered by a user is
/// checked with <see cref="Create"/>.
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    public Quantity(decimal value, UnitCode unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }

    public UnitCode Unit { get; }

    public bool IsZero => Value == 0m;

    public bool IsNegative => Value < 0m;

    /// <summary>Checks the entered value against the unit's decimals (UnitOfMeasure.decimals, R-INV-02).</summary>
    public static Result<Quantity> Create(decimal value, UnitCode unit, int unitDecimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unitDecimals);
        return DecimalRounding.Scale(value) > unitDecimals && DecimalRounding.Round(value, unitDecimals) != value
            ? Error.Validation("quantity_precision", $"Quantity {value} has more decimals than unit {unit} allows ({unitDecimals}).", "R-INV-02")
            : new Quantity(value, unit);
    }

    public static Quantity Zero(UnitCode unit) => new(0m, unit);

    public static Quantity operator +(Quantity left, Quantity right) => new(left.Value + right.Value, SameUnit(left, right));

    public static Quantity operator -(Quantity left, Quantity right) => new(left.Value - right.Value, SameUnit(left, right));

    public static Quantity operator -(Quantity value) => new(-value.Value, value.Unit);

    public static Quantity operator *(Quantity value, decimal factor) => new(value.Value * factor, value.Unit);

    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    public static Quantity Add(Quantity left, Quantity right) => left + right;

    public static Quantity Subtract(Quantity left, Quantity right) => left - right;

    public static Quantity Negate(Quantity value) => -value;

    public static Quantity Multiply(Quantity value, decimal factor) => value * factor;

    public int CompareTo(Quantity other)
    {
        _ = SameUnit(this, other);
        return Value.CompareTo(other.Value);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Value} {Unit}");

    private static UnitCode SameUnit(Quantity left, Quantity right) =>
        left.Unit == right.Unit
            ? left.Unit
            : throw new InvalidOperationException($"Cannot combine {left.Unit} with {right.Unit}; convert at the edge first (R-INV-02).");
}

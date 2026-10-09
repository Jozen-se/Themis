using System.Globalization;

namespace Themis.Kernel.Primitives;

/// <summary>
/// An exact decimal amount with its currency (R-MON-01). Arithmetic across currencies is refused. Rounding to the
/// currency's minor unit happens only where the caller asks for it, at the defined points (R-MON-02).
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public CurrencyCode Currency { get; }

    public bool IsZero => Amount == 0m;

    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount, SameCurrency(left, right));

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount, SameCurrency(left, right));

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    public static Money operator *(Money value, decimal factor) => new(value.Amount * factor, value.Currency);

    public static Money operator *(decimal factor, Money value) => value * factor;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public static Money Negate(Money value) => -value;

    public static Money Multiply(Money value, decimal factor) => value * factor;

    /// <summary>Rounds to the currency's minor units (R-MON-02). Unit prices and costs keep more decimals; this is for line, tax and document totals.</summary>
    public Money Round(int minorUnits, RoundingMode mode = RoundingMode.HalfAwayFromZero) =>
        new(DecimalRounding.Round(Amount, minorUnits, mode), Currency);

    public int CompareTo(Money other) => Amount.CompareTo(SameCurrencyAmount(this, other));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");

    private static CurrencyCode SameCurrency(Money left, Money right) =>
        left.Currency == right.Currency
            ? left.Currency
            : throw new InvalidOperationException($"Cannot combine {left.Currency} with {right.Currency}; every amount carries its own currency (R-MON-01).");

    private static decimal SameCurrencyAmount(Money left, Money right)
    {
        _ = SameCurrency(left, right);
        return right.Amount;
    }
}

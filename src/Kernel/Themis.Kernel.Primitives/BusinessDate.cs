using System.Globalization;

namespace Themis.Kernel.Primitives;

/// <summary>
/// A calendar date in the company's time zone (R-TIME-01). Business dates come from the command, never from the
/// system clock, so a posting date or document date is reproducible.
/// </summary>
public readonly record struct BusinessDate(DateOnly Value) : IComparable<BusinessDate>
{
    public static BusinessDate Parse(string isoDate) =>
        new(DateOnly.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture));

    public static bool operator <(BusinessDate left, BusinessDate right) => left.Value < right.Value;

    public static bool operator >(BusinessDate left, BusinessDate right) => left.Value > right.Value;

    public static bool operator <=(BusinessDate left, BusinessDate right) => left.Value <= right.Value;

    public static bool operator >=(BusinessDate left, BusinessDate right) => left.Value >= right.Value;

    public BusinessDate AddDays(int days) => new(Value.AddDays(days));

    public int CompareTo(BusinessDate other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

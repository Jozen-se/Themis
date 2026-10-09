using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Organisation;

/// <summary>
/// Builds the periods of a fiscal year (Phase 0 Specification, FiscalYear and FiscalPeriod): a year runs 12
/// months, or up to 18 for an extended first or changed year, and its periods cover it exactly, one calendar month
/// each, contiguous and without overlap. Business dates are calendar dates in the company's time zone (R-TIME-01).
/// </summary>
internal static class FiscalCalendar
{
    public const int MinMonths = 1;
    public const int MaxMonths = 18;

    /// <summary>The first day of the fiscal year that starts in <paramref name="startMonth"/> and is named after <paramref name="calendarYear"/>.</summary>
    public static BusinessDate YearStart(int calendarYear, int startMonth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startMonth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(startMonth, 12);
        return new BusinessDate(new DateOnly(calendarYear, startMonth, 1));
    }

    /// <summary>Monthly periods from <paramref name="yearStart"/>, numbered 1 to <paramref name="months"/>, each ending on the last day of its month.</summary>
    public static IReadOnlyList<FiscalPeriodDefinition> MonthlyPeriods(BusinessDate yearStart, int months, Func<int, Guid> periodId, Func<int, FiscalPeriodStatus> status)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, MinMonths);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(months, MaxMonths);
        ArgumentNullException.ThrowIfNull(periodId);
        ArgumentNullException.ThrowIfNull(status);
        if (yearStart.Value.Day != 1)
        {
            throw new ArgumentException("A fiscal year starts on the first day of a month.", nameof(yearStart));
        }

        List<FiscalPeriodDefinition> periods = new(months);
        for (int number = 1; number <= months; number++)
        {
            DateOnly start = yearStart.Value.AddMonths(number - 1);
            DateOnly end = start.AddMonths(1).AddDays(-1);
            periods.Add(new FiscalPeriodDefinition(periodId(number), number, new BusinessDate(start), new BusinessDate(end), status(number)));
        }

        return periods;
    }

    /// <summary>The last day of a year of <paramref name="months"/> months starting at <paramref name="yearStart"/>.</summary>
    public static BusinessDate YearEnd(BusinessDate yearStart, int months) => new(yearStart.Value.AddMonths(months).AddDays(-1));
}

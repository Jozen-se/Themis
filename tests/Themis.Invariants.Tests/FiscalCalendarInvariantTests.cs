using CsCheck;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Invariants.Tests;

/// <summary>For every start month and length, the periods of a fiscal year cover it exactly: contiguous, no gaps, no overlaps (Phase 0 Specification, FiscalPeriod).</summary>
[Trait("Category", "Invariant")]
public sealed class FiscalCalendarInvariantTests
{
    [Fact]
    public void Periods_cover_the_fiscal_year_exactly_for_every_start_month_and_length()
    {
        Gen.Select(Gen.Int[1990, 2100], Gen.Int[1, 12], Gen.Int[FiscalCalendar.MinMonths, FiscalCalendar.MaxMonths]).Sample((year, startMonth, months) =>
        {
            BusinessDate start = FiscalCalendar.YearStart(year, startMonth);
            IReadOnlyList<FiscalPeriodDefinition> periods = FiscalCalendar.MonthlyPeriods(start, months, _ => Guid.NewGuid(), _ => FiscalPeriodStatus.Open);
            bool contiguous = periods.Zip(periods.Skip(1)).All(pair => pair.First.EndDate.AddDays(1) == pair.Second.StartDate);
            bool numbered = periods.Select((period, index) => period.PeriodNumber == index + 1).All(ok => ok);
            return periods.Count == months
                && periods[0].StartDate == start
                && periods[^1].EndDate == FiscalCalendar.YearEnd(start, months)
                && contiguous
                && numbered
                && periods.All(period => period.StartDate <= period.EndDate && period.StartDate.Value.Day == 1);
        });
    }
}

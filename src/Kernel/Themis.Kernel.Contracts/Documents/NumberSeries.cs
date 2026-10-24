using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Documents;

/// <summary>Phase 0 Specification, NumberSeries: one running number per company and type, or one per fiscal year.</summary>
public enum NumberSeriesScope
{
    Continuous,
    PerFiscalYear,
}

/// <summary>
/// Phase 0 Specification, NumberSeries, as the test and development fixture defines a series (step 7 brief). The
/// fiscal year is required exactly when the scope is PerFiscalYear and belongs to the series' company; gapless
/// cannot be false where the registry requires it; padding is a minimum width, not a limit.
/// </summary>
public sealed record NumberSeriesDefinition(
    Guid Id,
    Guid? CompanyId,
    string DocumentType,
    NumberSeriesScope Scope,
    Guid? FiscalYearId,
    string? Prefix,
    int Padding,
    long NextNumber,
    bool Gapless);

/// <summary>The number series of a test and development fixture, written by the migrator's seeder.</summary>
public sealed record NumberSeriesFixtureDefinition(IReadOnlyList<NumberSeriesDefinition> Series);

/// <summary>A series a command needs a number from: the document type and, for a per-fiscal-year series, the fiscal year.</summary>
public sealed record NumberRequest(string DocumentType, Guid? FiscalYearId = null);

/// <summary>A number taken from a series: the formatted number and the sequence it came from.</summary>
public sealed record NumberTaken(NumberRequest Request, Guid SeriesId, string Number, long Sequence);

/// <summary>
/// Numbering (R-DOC-07, R-LED-08, Specification NumberSeries): every series, gapless or not, takes its numbers from
/// its locked series row, never from a database sequence. A command asks for every number it needs in one call;
/// the service locks the series rows in one statement in primary key order, as the last lock class of the lock
/// order, so the numbers of a failed transaction return with it and no gap appears. The caller orders its requests
/// so that the document number comes first and the journal entry number last before commit. Step 8 is the first
/// caller; step 7 proves the service with tests.
/// </summary>
public interface INumberSeriesService
{
    Task<Result<IReadOnlyList<NumberTaken>>> TakeAsync(IUnitOfWork unitOfWork, Guid companyId, IReadOnlyList<NumberRequest> requests, DateTimeOffset now, UserId takenBy, CancellationToken cancellationToken);

    /// <summary>
    /// The same for the installation-level series, those with an empty company (milestone 2 step 3, D-64): the PARTY
    /// series of party numbers. Continuous only, so a request never names a fiscal year; the same one-statement
    /// FOR UPDATE and the same rejection under R-DOC-08 when the series does not exist.
    /// </summary>
    Task<Result<IReadOnlyList<NumberTaken>>> TakeInstallationAsync(IUnitOfWork unitOfWork, IReadOnlyList<NumberRequest> requests, DateTimeOffset now, UserId takenBy, CancellationToken cancellationToken);
}

/// <summary>The one way a number is written (Specification, NumberSeries): the prefix, then the sequence padded to at least the padding width.</summary>
public static class NumberFormat
{
    public const int MaxPadding = 20;

    public static string Format(string? prefix, int padding, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(padding);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(padding, MaxPadding);
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        return prefix + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(padding, '0');
    }
}

using System.Data.Common;
using System.Globalization;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Documents;

/// <summary>
/// Numbering from locked series rows (R-DOC-07, R-LED-08; Specification, NumberSeries): the one mechanism for
/// every series, gapless or not, never a database sequence. The series a command needs are locked in one
/// <c>SELECT ... ORDER BY id FOR UPDATE</c>, primary key order, as the last lock class of the lock order; each
/// number is the series' next_number, formatted with the prefix and the padding as a minimum width (after
/// IA-999999 comes IA-1000000), and the row advances. A failed transaction returns its numbers with the rollback,
/// so no gap appears. A series the company does not have is refused with R-DOC-08, naming the type and, for a
/// per-fiscal-year series, the fiscal year; there is no fallback series or sequence (D-52).
/// </summary>
internal sealed class NumberSeriesService : INumberSeriesService
{
    public const string SeriesMissing = "number_series_missing";

    public async Task<Result<IReadOnlyList<NumberTaken>>> TakeAsync(IUnitOfWork unitOfWork, Guid companyId, IReadOnlyList<NumberRequest> requests, DateTimeOffset now, UserId takenBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return Array.Empty<NumberTaken>();
        }

        // One statement locks every series the command uses, in primary key order (R-LED-08, lock class NumberSeries).
        List<NumberRequest> distinct = [.. requests.Distinct()];
        Dictionary<string, object?> parameters = new() { ["company"] = companyId };
        List<string> conditions = [];
        for (int i = 0; i < distinct.Count; i++)
        {
            parameters[$"type{i}"] = distinct[i].DocumentType;
            if (distinct[i].FiscalYearId is { } fiscalYear)
            {
                parameters[$"year{i}"] = fiscalYear;
                conditions.Add($"(document_type = @type{i} AND fiscal_year_id = @year{i})");
            }
            else
            {
                conditions.Add($"(document_type = @type{i} AND fiscal_year_id IS NULL)");
            }
        }

        IReadOnlyList<SeriesRow> rows = await unitOfWork.QueryAsync(DatabaseRole.Kernel,
            $"""
            SELECT id, document_type, fiscal_year_id, prefix, padding, next_number
            FROM kernel.number_series
            WHERE company_id = @company AND ({string.Join(" OR ", conditions)})
            ORDER BY id
            FOR UPDATE
            """,
            parameters,
            Read,
            cancellationToken);

        Dictionary<NumberRequest, SeriesRow> bySeries = rows.ToDictionary(row => new NumberRequest(row.DocumentType, row.FiscalYearId));
        foreach (NumberRequest request in distinct)
        {
            if (!bySeries.ContainsKey(request))
            {
                string year = request.FiscalYearId is { } fiscalYear ? $" for fiscal year {fiscalYear}" : string.Empty;
                // R-DOC-08: a command that assigns a number requires the series for its type and scope; there is no fallback (D-52).
                return Error.Rule("R-DOC-08", SeriesMissing, $"Company {companyId} has no {request.DocumentType} number series{year}.");
            }
        }

        List<NumberTaken> taken = [];
        foreach (NumberRequest request in requests)
        {
            SeriesRow series = bySeries[request];
            long sequence = series.NextNumber;
            taken.Add(new NumberTaken(request, series.Id, NumberFormat.Format(series.Prefix, series.Padding, sequence), sequence));
            bySeries[request] = series with { NextNumber = sequence + 1 };
        }

        foreach (SeriesRow series in bySeries.Values)
        {
            await unitOfWork.ExecuteAsync(DatabaseRole.Kernel,
                "UPDATE kernel.number_series SET next_number = @next, updated_at = @now, updated_by = @by, version = version + 1 WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = series.Id, ["next"] = series.NextNumber, ["now"] = now, ["by"] = takenBy.Value },
                cancellationToken);
        }

        return taken;
    }

    private static SeriesRow Read(DbDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetGuid(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt32(4),
        reader.GetInt64(5));

    private sealed record SeriesRow(Guid Id, string DocumentType, Guid? FiscalYearId, string? Prefix, int Padding, long NextNumber)
    {
        public override string ToString() => $"{DocumentType} {NextNumber.ToString(CultureInfo.InvariantCulture)}";
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// RFC 9457 problem details with the Themis members <c>ruleId</c> and <c>code</c> (R-API-03). <c>errors</c> carries
/// every error of a failed result; the top-level members repeat the primary one. The correlation id lets a caller
/// quote the request in support.
/// </summary>
internal sealed record ProblemDetailsBody(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Code,
    string? RuleId,
    string CorrelationId,
    IReadOnlyList<ProblemError> Errors)
{
    public const string BlankType = "about:blank";

    public static CommandResponse ToResponse(IReadOnlyList<Error> errors, CorrelationId correlationId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(errors.Count);
        Error primary = errors[0];
        int status = StatusFor(primary.Kind);
        ProblemDetailsBody body = new(BlankType, TitleFor(status), status, primary.Message, primary.Code, primary.RuleId, correlationId.Value,
            [.. errors.Select(error => new ProblemError(error.Code, error.RuleId, error.Message, error.Details))]);
        return new CommandResponse(status, CommandResponse.ProblemContentType, JsonSerializer.Serialize(body, ThemisJson.ProblemOptions), Replayed: false);
    }

    public static CommandResponse ToResponse(Error error, CorrelationId correlationId) => ToResponse([error], correlationId);

    /// <summary>The HTTP status each error kind maps to; the kind comes from the handler, the status from here only.</summary>
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorKind.RuleViolation => StatusCodes.Status422UnprocessableEntity,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown error kind."),
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status412PreconditionFailed => "Precondition Failed",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Content",
        StatusCodes.Status428PreconditionRequired => "Precondition Required",
        _ => "Error",
    };

    /// <summary>A problem the HTTP layer itself raises before dispatch (missing headers, unreadable body).</summary>
    public static CommandResponse Http(int status, string code, string? ruleId, string detail, CorrelationId correlationId)
    {
        ProblemDetailsBody body = new(BlankType, TitleFor(status), status, detail, code, ruleId, correlationId.Value, [new ProblemError(code, ruleId, detail, new Dictionary<string, string>())]);
        return new CommandResponse(status, CommandResponse.ProblemContentType, JsonSerializer.Serialize(body, ThemisJson.ProblemOptions), Replayed: false);
    }
}

internal sealed record ProblemError(string Code, string? RuleId, string Message, IReadOnlyDictionary<string, string> Details);

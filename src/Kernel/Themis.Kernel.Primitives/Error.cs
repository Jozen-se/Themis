using System.Text.RegularExpressions;

namespace Themis.Kernel.Primitives;

/// <summary>What kind of failure an <see cref="Error"/> is; the API maps each kind to an HTTP status (R-API-03).</summary>
public enum ErrorKind
{
    /// <summary>The request is malformed or fails a validation rule (400).</summary>
    Validation,

    /// <summary>The principal lacks the permission (403, R-SEC-01).</summary>
    Forbidden,

    /// <summary>The record does not exist (404).</summary>
    NotFound,

    /// <summary>The request conflicts with concurrent work (409).</summary>
    Conflict,

    /// <summary>The record version in If-Match is stale (412, R-API-02).</summary>
    PreconditionFailed,

    /// <summary>A business rule rejects the command (422).</summary>
    RuleViolation,
}

/// <summary>
/// A rejection with a stable code and the ID of the rule that failed (R-API-03). Codes are snake_case; rule IDs
/// follow the design documents' vocabulary. The name is the brief's (Result and Error carrying rule IDs).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The Build Brief names the type Error.")]
public sealed partial record Error
{
    private Error(string code, string? ruleId, string message, ErrorKind kind, IReadOnlyDictionary<string, string>? details)
    {
        Code = ValidCode(code);
        RuleId = ruleId is null ? null : ValidRuleId(ruleId);
        Message = message;
        Kind = kind;
        Details = details ?? new Dictionary<string, string>();
    }

    public string Code { get; }

    public string? RuleId { get; }

    public string Message { get; }

    public ErrorKind Kind { get; }

    public IReadOnlyDictionary<string, string> Details { get; }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^(?:R-[A-Z]{2,4}-\d{2}|[PDIE]-\d{2}|ARCH-\d{2})$")]
    private static partial Regex RuleIdPattern();

    public static Error Validation(string code, string message, string? ruleId = null, IReadOnlyDictionary<string, string>? details = null) =>
        new(code, ruleId, message, ErrorKind.Validation, details);

    public static Error Forbidden(string code, string message, string ruleId = "R-SEC-01") =>
        new(code, ruleId, message, ErrorKind.Forbidden, null);

    public static Error NotFound(string code, string message) => new(code, null, message, ErrorKind.NotFound, null);

    public static Error Conflict(string code, string message, string? ruleId = null) =>
        new(code, ruleId, message, ErrorKind.Conflict, null);

    public static Error PreconditionFailed(string code, string message, string ruleId = "R-API-02") =>
        new(code, ruleId, message, ErrorKind.PreconditionFailed, null);

    public static Error Rule(string ruleId, string code, string message, IReadOnlyDictionary<string, string>? details = null) =>
        new(code, ruleId, message, ErrorKind.RuleViolation, details);

    public override string ToString() => RuleId is null ? $"{Code}: {Message}" : $"{Code} ({RuleId}): {Message}";

    private static string ValidCode(string code) =>
        CodePattern().IsMatch(code) ? code : throw new ArgumentException($"'{code}' is not a snake_case error code.", nameof(code));

    private static string ValidRuleId(string ruleId) =>
        RuleIdPattern().IsMatch(ruleId) ? ruleId : throw new ArgumentException($"'{ruleId}' is not a design rule ID.", nameof(ruleId));
}

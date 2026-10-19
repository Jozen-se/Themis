namespace Themis.Kernel.Contracts.Commands;

/// <summary>
/// A business key whose unique constraint in the database stands behind a rule (RR-45): when two commands race past
/// the handler's own check and the database refuses the second with a unique violation (SQLSTATE 23505) on this
/// constraint, the pipeline turns it into the rule's rejection, stored as the key's result like any other (R-API-03),
/// instead of a 500. A capability registers one per constraint; an unmapped unique violation stays an error.
/// </summary>
public sealed record UniqueConstraintRule(string ConstraintName, string RuleId, string Code, string Message);

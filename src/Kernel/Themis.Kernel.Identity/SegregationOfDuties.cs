using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Identity;

/// <summary>
/// The segregation-of-duties check (R-SEC-02) as a pure function over permission sets: a role, or the union of
/// the roles one user holds in one company, may not contain both permissions of a conflict pair. The fixture seeder
/// runs it before any insert; the commands that manage roles and assignments will run it when they exist.
/// </summary>
internal static class SegregationOfDuties
{
    public const string RuleId = "R-SEC-02";

    /// <summary>The conflict pairs both of whose permissions the set holds; empty when the set is allowed.</summary>
    public static IReadOnlyList<SodConflictDefinition> Violations(IEnumerable<string> permissions, IEnumerable<SodConflictDefinition> conflicts)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(conflicts);
        HashSet<string> held = new(permissions, StringComparer.Ordinal);
        return [.. conflicts.Where(pair => held.Contains(pair.PermissionA) && held.Contains(pair.PermissionB))];
    }

    /// <summary>Rejects a permission set that holds a conflict pair, naming the pair (R-SEC-02).</summary>
    public static Result Check(string subject, IEnumerable<string> permissions, IEnumerable<SodConflictDefinition> conflicts)
    {
        IReadOnlyList<SodConflictDefinition> violations = Violations(permissions, conflicts);
        return violations.Count == 0
            ? Result.Success()
            : Error.Rule(RuleId, "segregation_of_duties_conflict", $"{subject} would hold conflicting permissions: {string.Join("; ", violations.Select(pair => $"{pair.PermissionA} with {pair.PermissionB}"))}.");
    }

    /// <summary>
    /// Checks a whole fixture: each role on its own, then each user's union of permissions per company over the
    /// roles assigned (R-SEC-02: a user may not hold both in the same company).
    /// </summary>
    public static Result CheckFixture(IdentityFixtureDefinition fixture, IEnumerable<SodConflictDefinition> conflicts)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        List<SodConflictDefinition> pairs = [.. conflicts];
        Dictionary<Guid, RoleDefinition> roles = fixture.Roles.ToDictionary(role => role.Id);
        foreach (RoleDefinition role in fixture.Roles)
        {
            Result roleCheck = Check($"Role {role.Code}", role.Permissions, pairs);
            if (!roleCheck.IsSuccess)
            {
                return roleCheck;
            }
        }

        foreach (IGrouping<(Guid UserId, Guid CompanyId), RoleAssignmentDefinition> group in fixture.Assignments.GroupBy(assignment => (assignment.UserId, assignment.CompanyId)))
        {
            IEnumerable<string> held = group.SelectMany(assignment => roles.TryGetValue(assignment.RoleId, out RoleDefinition? role) ? role.Permissions : []);
            Result userCheck = Check($"User {group.Key.UserId} in company {group.Key.CompanyId}", held, pairs);
            if (!userCheck.IsSuccess)
            {
                return userCheck;
            }
        }

        return Result.Success();
    }
}

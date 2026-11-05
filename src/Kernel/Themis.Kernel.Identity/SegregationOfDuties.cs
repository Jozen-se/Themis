using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Identity;

/// <summary>
/// The segregation-of-duties check (R-SEC-02, D-70) as a pure function over permission sets: a role, or the union of
/// the roles one user holds in one company, may not contain both permissions of a conflict pair; a pair with an
/// installation-scope permission is checked over all of the user's assignments. The fixture seeder runs it before any
/// insert; the commands that manage roles and assignments (step 13) will run the same function.
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
    /// Checks a whole fixture: each role on its own, then the roles each user is assigned (R-SEC-02, D-70), through
    /// <see cref="CheckAssignments"/>, the function the step 13 role assignment commands reuse.
    /// </summary>
    public static Result CheckFixture(IdentityFixtureDefinition fixture, PermissionCatalogDefinition catalogue)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(catalogue);
        foreach (RoleDefinition role in fixture.Roles)
        {
            Result roleCheck = Check($"Role {role.Code}", role.Permissions, catalogue.Conflicts);
            if (!roleCheck.IsSuccess)
            {
                return roleCheck;
            }
        }

        Dictionary<Guid, RoleDefinition> roles = fixture.Roles.ToDictionary(role => role.Id);
        return CheckAssignments(
            fixture.Assignments.Select(assignment => new HeldPermissions(assignment.UserId, assignment.CompanyId, roles.TryGetValue(assignment.RoleId, out RoleDefinition? role) ? role.Permissions : [])),
            catalogue);
    }

    /// <summary>
    /// R-SEC-02 and D-70 over the permissions users hold through their assignments: a user may not hold both permissions
    /// of a pair in the same company; when either permission of the pair is installation-scope (it acts in every company,
    /// RR-52), the user may not hold both anywhere in the installation, so the pair is checked over all of the user's
    /// assignments together. A pair of two company-scope permissions held in different companies stays allowed.
    /// </summary>
    public static Result CheckAssignments(IEnumerable<HeldPermissions> held, PermissionCatalogDefinition catalogue)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(catalogue);
        List<HeldPermissions> rows = [.. held];
        HashSet<string> installationScope = new(catalogue.Permissions.Where(permission => permission.InstallationScope).Select(permission => permission.Code), StringComparer.Ordinal);
        List<SodConflictDefinition> companyPairs = [.. catalogue.Conflicts.Where(pair => !installationScope.Contains(pair.PermissionA) && !installationScope.Contains(pair.PermissionB))];
        List<SodConflictDefinition> installationPairs = [.. catalogue.Conflicts.Where(pair => installationScope.Contains(pair.PermissionA) || installationScope.Contains(pair.PermissionB))];

        foreach (IGrouping<(Guid UserId, Guid CompanyId), HeldPermissions> group in rows.GroupBy(row => (row.UserId, row.CompanyId)))
        {
            Result userCheck = Check($"User {group.Key.UserId} in company {group.Key.CompanyId}", group.SelectMany(row => row.Permissions), companyPairs);
            if (!userCheck.IsSuccess)
            {
                return userCheck;
            }
        }

        foreach (IGrouping<Guid, HeldPermissions> group in rows.GroupBy(row => row.UserId))
        {
            Result userCheck = Check($"User {group.Key} across the installation", group.SelectMany(row => row.Permissions), installationPairs);
            if (!userCheck.IsSuccess)
            {
                return userCheck;
            }
        }

        return Result.Success();
    }
}

/// <summary>The permissions one user holds in one company through one assignment (R-SEC-01: the assignment carries the scope).</summary>
internal sealed record HeldPermissions(Guid UserId, Guid CompanyId, IReadOnlyList<string> Permissions);

using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Identity;

/// <summary>R-SEC-02 on the pure check and on the milestone 1 fixture (step 4 brief, acceptance 6).</summary>
[Trait("Category", "Unit")]
public sealed class SegregationOfDutiesTests
{
    private static readonly IReadOnlyList<SodConflictDefinition> Conflicts = PermissionCatalog.Definition.Conflicts;

    [Fact]
    public void R_SEC_02_a_role_that_combines_a_conflict_pair_is_refused_with_the_pair_named()
    {
        Result refused = SegregationOfDuties.Check("Role TEST", ["ACCOUNT.MAINTAIN", "MANUAL_JOURNAL.POST", "ITEM.CREATE"], Conflicts);
        Result allowed = SegregationOfDuties.Check("Role TEST", ["ACCOUNT.MAINTAIN", "ITEM.CREATE"], Conflicts);

        Assert.True(refused.IsFailure);
        Assert.Equal("R-SEC-02", refused.Primary!.RuleId);
        Assert.Equal("segregation_of_duties_conflict", refused.Primary.Code);
        Assert.Contains("ACCOUNT.MAINTAIN with MANUAL_JOURNAL.POST", refused.Primary.Message, StringComparison.Ordinal);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public void R_SEC_02_two_roles_assigned_to_one_user_in_one_company_may_not_combine_a_pair_but_may_across_companies()
    {
        Guid user = Guid.NewGuid();
        Guid companyA = Guid.NewGuid();
        Guid companyB = Guid.NewGuid();
        RoleDefinition reopen = new(Guid.NewGuid(), Code.From("REOPEN"), "Reopens periods", ["FISCAL_PERIOD.REOPEN"]);
        RoleDefinition post = new(Guid.NewGuid(), Code.From("POST"), "Posts journals", ["MANUAL_JOURNAL.POST"]);
        UserDefinition userDefinition = new(user, "subject", "Test", null, false, UserStatus.Active);
        BusinessDate from = BusinessDate.Parse("2026-01-01");

        IdentityFixtureDefinition sameCompany = new([userDefinition], [reopen, post],
        [
            new RoleAssignmentDefinition(Guid.NewGuid(), user, reopen.Id, companyA, null, from, null),
            new RoleAssignmentDefinition(Guid.NewGuid(), user, post.Id, companyA, null, from, null),
        ]);
        IdentityFixtureDefinition acrossCompanies = sameCompany with
        {
            Assignments = [sameCompany.Assignments[0], sameCompany.Assignments[1] with { CompanyId = companyB }],
        };

        Assert.True(SegregationOfDuties.CheckFixture(sameCompany, Conflicts).IsFailure);
        Assert.True(SegregationOfDuties.CheckFixture(acrossCompanies, Conflicts).IsSuccess);
    }

    [Fact]
    public void The_milestone_1_fixture_passes_the_check_and_gives_the_clerk_POST_and_ITEM_CREATE_and_the_preparer_neither()
    {
        IdentityFixtureDefinition fixture = Milestone1IdentityFixture.For(Milestone1OrganisationFixture.CompanyId);

        Assert.True(SegregationOfDuties.CheckFixture(fixture, Conflicts).IsSuccess);
        Assert.Equal(2, fixture.Users.Count);
        Dictionary<Guid, RoleDefinition> roles = fixture.Roles.ToDictionary(role => role.Id);
        HashSet<string> clerk = [.. fixture.Assignments.Where(a => a.UserId == Milestone1IdentityFixture.ClerkId).SelectMany(a => roles[a.RoleId].Permissions)];
        HashSet<string> preparer = [.. fixture.Assignments.Where(a => a.UserId == Milestone1IdentityFixture.PreparerId).SelectMany(a => roles[a.RoleId].Permissions)];

        Assert.Superset(new HashSet<string> { "ITEM.CREATE", "INVENTORY_ADJUSTMENT.CREATE", "INVENTORY_ADJUSTMENT.UPDATE", "INVENTORY_ADJUSTMENT.POST", "INVENTORY_ADJUSTMENT.DISCARD" }, clerk);
        Assert.DoesNotContain("INVENTORY_ADJUSTMENT.POST", preparer);
        Assert.DoesNotContain("ITEM.CREATE", preparer);
        Assert.Contains("ITEM.READ", preparer);
        Assert.Equal(clerk.Where(p => p is not "INVENTORY_ADJUSTMENT.POST" and not "ITEM.CREATE").Order(), preparer.Order());
        Assert.All(fixture.Assignments, assignment => Assert.Null(assignment.SiteId));
        Assert.All(fixture.Assignments, assignment => Assert.Equal(Milestone1OrganisationFixture.CompanyId, assignment.CompanyId));
        Assert.All(fixture.Roles.SelectMany(role => role.Permissions), permission => Assert.True(PermissionCatalog.Contains(permission)));
    }
}

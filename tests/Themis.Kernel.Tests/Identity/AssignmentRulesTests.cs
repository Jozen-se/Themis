using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Identity;

/// <summary>The scope and validity rules of R-SEC-01 as the step 4 brief and D-47 state them, on the pure function.</summary>
[Trait("Category", "Unit")]
public sealed class AssignmentRulesTests
{
    private static readonly Guid CompanyA = Guid.Parse("00000000-0000-7000-8000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("00000000-0000-7000-8000-00000000000b");
    private static readonly Guid Site1 = Guid.Parse("00000000-0000-7000-8000-000000000011");
    private static readonly Guid Site2 = Guid.Parse("00000000-0000-7000-8000-000000000012");
    private static readonly DateTimeOffset Noon = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static PermissionAssignment Assignment(Guid company, Guid? site = null, string validFrom = "2026-01-01", string? validTo = null) =>
        new(company, site, BusinessDate.Parse(validFrom), validTo is null ? null : BusinessDate.Parse(validTo), "Europe/Stockholm");

    [Fact]
    public void R_SEC_01_a_company_wide_assignment_covers_the_company_and_every_site_of_it()
    {
        PermissionAssignment[] held = [Assignment(CompanyA)];

        Assert.Equal(AuthorizationOutcome.Allowed, AssignmentRules.Evaluate(held, RequestScope.ForCompany(CompanyA), Noon));
        Assert.Equal(AuthorizationOutcome.Allowed, AssignmentRules.Evaluate(held, RequestScope.ForSite(CompanyA, Site1), Noon));
        Assert.Equal(AuthorizationOutcome.Allowed, AssignmentRules.Evaluate(held, RequestScope.Installation, Noon));
        Assert.Equal(AuthorizationOutcome.OutOfScope, AssignmentRules.Evaluate(held, RequestScope.ForCompany(CompanyB), Noon));
        Assert.Equal(AuthorizationOutcome.OutOfScope, AssignmentRules.Evaluate(held, RequestScope.ForSite(CompanyB, Site1), Noon));
    }

    [Fact]
    public void D_47_a_site_limited_assignment_covers_its_site_only_and_never_a_request_without_a_site()
    {
        PermissionAssignment[] held = [Assignment(CompanyA, Site1)];

        Assert.Equal(AuthorizationOutcome.Allowed, AssignmentRules.Evaluate(held, RequestScope.ForSite(CompanyA, Site1), Noon));
        Assert.Equal(AuthorizationOutcome.OutOfScope, AssignmentRules.Evaluate(held, RequestScope.ForSite(CompanyA, Site2), Noon));
        Assert.Equal(AuthorizationOutcome.OutOfScope, AssignmentRules.Evaluate(held, RequestScope.ForCompany(CompanyA), Noon));
        Assert.Equal(AuthorizationOutcome.OutOfScope, AssignmentRules.Evaluate(held, RequestScope.Installation, Noon));
    }

    [Fact]
    public void R_SEC_01_no_assignment_carrying_the_permission_is_permission_missing_not_out_of_scope()
    {
        Assert.Equal(AuthorizationOutcome.PermissionMissing, AssignmentRules.Evaluate([], RequestScope.ForCompany(CompanyA), Noon));
    }

    [Fact]
    public void R_TIME_01_validity_is_judged_on_the_business_date_in_the_company_time_zone_with_valid_to_exclusive()
    {
        // 2026-06-30T22:30Z is already 2026-07-01 in Stockholm.
        DateTimeOffset lateEvening = new(2026, 6, 30, 22, 30, 0, TimeSpan.Zero);
        PermissionAssignment endsJuly = Assignment(CompanyA, validTo: "2026-07-01");
        PermissionAssignment startsJuly = Assignment(CompanyA, validFrom: "2026-07-01");

        Assert.False(AssignmentRules.IsValidOn(endsJuly, lateEvening));
        Assert.True(AssignmentRules.IsValidOn(endsJuly, new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero)));
        Assert.True(AssignmentRules.IsValidOn(startsJuly, lateEvening));
        Assert.False(AssignmentRules.IsValidOn(startsJuly, new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void R_SEC_01_an_expired_assignment_counts_as_missing_and_a_valid_one_elsewhere_as_out_of_scope()
    {
        PermissionAssignment[] expired = [Assignment(CompanyA, validFrom: "2025-01-01", validTo: "2026-01-01")];
        PermissionAssignment[] expiredHereValidThere = [Assignment(CompanyA, validTo: "2026-01-01"), Assignment(CompanyB)];

        Assert.Equal(AuthorizationOutcome.PermissionMissing, AssignmentRules.Evaluate(expired, RequestScope.ForCompany(CompanyA), Noon));
        Assert.Equal(AuthorizationOutcome.OutOfScope, AssignmentRules.Evaluate(expiredHereValidThere, RequestScope.ForCompany(CompanyA), Noon));
    }

    [Fact]
    public void A_request_scope_names_what_it_covers()
    {
        Assert.Equal(ScopeKind.Installation, RequestScope.Installation.Kind);
        Assert.Equal(ScopeKind.Site, RequestScope.ForSite(CompanyA, Site1).Kind);
        Assert.Throws<ArgumentException>(() => RequestScope.ForCompany(Guid.Empty));
        Assert.Throws<ArgumentException>(() => RequestScope.ForSite(CompanyA, Guid.Empty));
        Assert.Equal($"company {CompanyA}", RequestScope.ForCompany(CompanyA).ToString());
    }

    [Fact]
    public void R_TIME_01_a_UTC_instant_becomes_the_calendar_date_of_the_zone()
    {
        Assert.Equal(BusinessDate.Parse("2026-02-01"), BusinessDate.InZone(new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.Zero), "Europe/Stockholm"));
        Assert.Equal(BusinessDate.Parse("2026-01-31"), BusinessDate.InZone(new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
    }
}

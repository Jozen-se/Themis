using Npgsql;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;
using Themis.Kernel.Persistence.UnitOfWork;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Identity;

/// <summary>
/// The access control of step 4 on a unit of work: subjects become principals through their User row, and the
/// permission is held through an assignment in the request's scope, valid on the company's business date
/// (R-SEC-01, R-TIME-01, D-47); each refusal carries its own code.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class AccessControlTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _db;
    private readonly AccessControl _accessControl = new();
    private IdentityTestData _data = null!;
    private NpgsqlDataSource _dataSource = null!;

    public AccessControlTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _data = await IdentityTestData.CreateAsync(_db, Ct);
        _dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _data.DisposeAsync();
    }

    [Fact]
    public async Task A_known_subject_becomes_its_principal_and_an_unknown_the_disabled_and_the_system_subject_are_refused()
    {
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);

        Result<Principal> clerk = await _accessControl.ResolvePrincipalAsync(unitOfWork, new AuthenticatedSubject(Milestone1IdentityFixture.ClerkSubject), Ct);
        Result<Principal> unknown = await _accessControl.ResolvePrincipalAsync(unitOfWork, new AuthenticatedSubject("nobody-" + Guid.NewGuid()), Ct);
        Result<Principal> disabled = await _accessControl.ResolvePrincipalAsync(unitOfWork, new AuthenticatedSubject(_data.DisabledSubject), Ct);
        Result<Principal> system = await _accessControl.ResolvePrincipalAsync(unitOfWork, new AuthenticatedSubject(WellKnownUsers.SystemSubject), Ct);

        Assert.True(clerk.IsSuccess);
        Assert.Equal(Milestone1IdentityFixture.ClerkId, clerk.Value.UserId.Value);
        Assert.Equal("Clerk Carlsson", clerk.Value.DisplayName);
        Assert.False(clerk.Value.IsServiceAccount);
        Assert.Equal(AccessDenied.SubjectUnknown, unknown.Primary!.Code);
        Assert.Equal(AccessDenied.UserDisabled, disabled.Primary!.Code);
        Assert.Equal(AccessDenied.SubjectUnknown, system.Primary!.Code);
        Assert.All(new[] { unknown, disabled, system }, result => Assert.Equal("R-SEC-01", result.Primary!.RuleId));
        Assert.All(new[] { unknown, disabled, system }, result => Assert.Equal(ErrorKind.Forbidden, result.Primary!.Kind));
        // The reads ran as themis_kernel (themis_app is refused on kernel.user_account); RR-62: the service hands the connection back as themis_app.
        Assert.Equal("none", DatabaseRoleOf(unitOfWork));
    }

    [Fact]
    public async Task R_SEC_01_the_clerk_holds_POST_and_ITEM_CREATE_in_SE01_and_the_preparer_holds_neither()
    {
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);
        Principal clerk = new(new UserId(Milestone1IdentityFixture.ClerkId), "clerk", false);
        Principal preparer = new(new UserId(Milestone1IdentityFixture.PreparerId), "preparer", false);
        RequestScope company = RequestScope.ForCompany(IdentityTestData.Company);

        Assert.True((await _accessControl.AuthorizeAsync(unitOfWork, clerk, "INVENTORY_ADJUSTMENT.POST", company, Now, Ct)).IsSuccess);
        Assert.True((await _accessControl.AuthorizeAsync(unitOfWork, clerk, "INVENTORY_ADJUSTMENT.POST", RequestScope.ForSite(IdentityTestData.Company, IdentityTestData.MainSite), Now, Ct)).IsSuccess);
        Assert.True((await _accessControl.AuthorizeAsync(unitOfWork, clerk, "ITEM.CREATE", company, Now, Ct)).IsSuccess);
        Assert.True((await _accessControl.AuthorizeAsync(unitOfWork, preparer, "INVENTORY_ADJUSTMENT.CREATE", company, Now, Ct)).IsSuccess);
        Result refused = await _accessControl.AuthorizeAsync(unitOfWork, preparer, "INVENTORY_ADJUSTMENT.POST", company, Now, Ct);
        Result noItemCreate = await _accessControl.AuthorizeAsync(unitOfWork, preparer, "ITEM.CREATE", company, Now, Ct);
        Assert.Equal(AccessDenied.PermissionMissing, refused.Primary!.Code);
        Assert.Equal("R-SEC-01", refused.Primary.RuleId);
        Assert.Equal(AccessDenied.PermissionMissing, noItemCreate.Primary!.Code);
    }

    [Fact]
    public async Task R_SEC_01_another_company_another_site_and_an_expired_assignment_are_each_refused_with_their_code()
    {
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(_dataSource, Ct);
        Principal clerk = new(new UserId(Milestone1IdentityFixture.ClerkId), "clerk", false);
        Principal otherSite = new(new UserId(_data.OtherSiteUser), "other site", false);
        Principal expired = new(new UserId(_data.ExpiredUser), "expired", false);

        Result otherCompany = await _accessControl.AuthorizeAsync(unitOfWork, clerk, "ITEM.CREATE", RequestScope.ForCompany(Guid.NewGuid()), Now, Ct);
        Result siteLimitedAtMain = await _accessControl.AuthorizeAsync(unitOfWork, otherSite, "INVENTORY_ADJUSTMENT.POST", RequestScope.ForSite(IdentityTestData.Company, IdentityTestData.MainSite), Now, Ct);
        Result siteLimitedCompanyWide = await _accessControl.AuthorizeAsync(unitOfWork, otherSite, "INVENTORY_ADJUSTMENT.POST", RequestScope.ForCompany(IdentityTestData.Company), Now, Ct);
        Result siteLimitedAtItsSite = await _accessControl.AuthorizeAsync(unitOfWork, otherSite, "INVENTORY_ADJUSTMENT.POST", RequestScope.ForSite(IdentityTestData.Company, _data.OtherSite), Now, Ct);
        Result expiredToday = await _accessControl.AuthorizeAsync(unitOfWork, expired, "INVENTORY_ADJUSTMENT.CREATE", RequestScope.ForCompany(IdentityTestData.Company), Now, Ct);
        Result validInJanuary = await _accessControl.AuthorizeAsync(unitOfWork, expired, "INVENTORY_ADJUSTMENT.CREATE", RequestScope.ForCompany(IdentityTestData.Company), new DateTimeOffset(2026, 1, 31, 22, 0, 0, TimeSpan.Zero), Ct);
        // 2026-01-31T23:30Z is already 2026-02-01 in Stockholm: valid_to is exclusive on the business date (R-TIME-01).
        Result expiredAtStockholmMidnight = await _accessControl.AuthorizeAsync(unitOfWork, expired, "INVENTORY_ADJUSTMENT.CREATE", RequestScope.ForCompany(IdentityTestData.Company), new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.Zero), Ct);

        Assert.Equal(AccessDenied.OutOfScope, otherCompany.Primary!.Code);
        Assert.Equal(AccessDenied.OutOfScope, siteLimitedAtMain.Primary!.Code);
        Assert.Equal(AccessDenied.OutOfScope, siteLimitedCompanyWide.Primary!.Code);
        Assert.True(siteLimitedAtItsSite.IsSuccess);
        Assert.Equal(AccessDenied.PermissionMissing, expiredToday.Primary!.Code);
        Assert.True(validInJanuary.IsSuccess);
        Assert.Equal(AccessDenied.PermissionMissing, expiredAtStockholmMidnight.Primary!.Code);
    }

    private static string DatabaseRoleOf(NpgsqlUnitOfWork unitOfWork) => unitOfWork.CurrentRole?.Name ?? "none";
}

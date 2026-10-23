using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.UnitOfWork;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// Role per statement (RR-62, R-DB-01): the dispatcher resets the role to themis_app before each call into module
/// code, every kernel Contracts service resets it on successful return, and a module reaches the connection only
/// through ForRoleAsync. A module statement that does not switch after a kernel call is refused; the module's data
/// context switches through its interceptor and succeeds. A D-41 retry and an RR-45 mapped rejection behave as before.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class RolePerStatementTests : IAsyncLifetime
{
    private const string Counters = "/v1/fixture/counters";
    private const string Items = "/v1/kernel/items";

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid Warehouse = Milestone1OrganisationFixture.WarehouseId;

    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public RolePerStatementTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync()
    {
        _host.ItemCreateSeam.AfterDuplicateCheck = null;
        await _db.TruncateAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task RR_62_the_scope_resolver_and_the_handler_each_start_as_themis_app()
    {
        CommandReply reply = await _host.PostAsync(RoleProbeRoutes.Probe, new { companyId = Company }, "rr62-probe", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal("themis_app", reply.Json!["scopeResolverUser"]!.GetValue<string>());
        // The advisory lock and the idempotency look-up ran as themis_kernel just before the handler.
        Assert.Equal("themis_app", reply.Json!["handlerUser"]!.GetValue<string>());
    }

    [Fact]
    public async Task RR_62_a_raw_statement_on_the_ForRoleAsync_connection_after_a_kernel_service_call_is_refused_with_42501()
    {
        Guid counter = await CreateCounterAsync("rr62-raw-create");

        CommandReply reply = await _host.PostAsync($"{Counters}/{counter}/probe-role", new { throughEntityFramework = false }, "rr62-raw", cancellationToken: Ct);

        Assert.Equal(500, reply.Status);
        Assert.Contains(_host.Logs.Lines, line => line.Level == LogLevel.Error && line.Message.Contains("42501", StringComparison.Ordinal) && line.Message.Contains("fixture", StringComparison.Ordinal));
        Assert.DoesNotContain(_host.Logs.Lines, line => line.Message.Contains("must be refused as themis_app", StringComparison.Ordinal));
        Assert.Equal(0L, await _host.ScalarAsync("SELECT count(*) FROM kernel.idempotency_record WHERE key = 'rr62-raw'", Ct));
    }

    [Fact]
    public async Task RR_62_the_same_read_through_the_fixture_data_context_and_its_interceptor_succeeds()
    {
        Guid counter = await CreateCounterAsync("rr62-ef-create");

        CommandReply reply = await _host.PostAsync($"{Counters}/{counter}/probe-role", new { throughEntityFramework = true }, "rr62-ef", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal(5, reply.Json!["value"]!.GetValue<long>());
    }

    [Fact]
    public async Task RR_62_every_kernel_Contracts_service_hands_the_connection_back_as_themis_app()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
        await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(dataSource, Ct);
        BusinessDate june = new(new DateOnly(2026, 6, 15));
        List<(string Call, string User)> seen = [];

        async Task After(string call)
        {
            seen.Add((call, await RoleProbeRoutes.CurrentUserAsync(unitOfWork, Ct)));
        }

        IOrganisationReader organisation = services.GetRequiredService<IOrganisationReader>();
        await organisation.GetCompanyAsync(unitOfWork, Company, Ct);
        await After("IOrganisationReader.GetCompanyAsync");
        await organisation.GetWarehouseAsync(unitOfWork, Warehouse, Ct);
        await After("IOrganisationReader.GetWarehouseAsync");
        await organisation.ResolveFiscalPeriodAsync(unitOfWork, Company, june, Ct);
        await After("IOrganisationReader.ResolveFiscalPeriodAsync");

        ISettingsReader settings = services.GetRequiredService<ISettingsReader>();
        await settings.ResolveAsync(unitOfWork, SettingKeys.DocumentMaxLines, SettingTarget.ForCompany(Company), Ct);
        await After("ISettingsReader.ResolveAsync");
        await settings.GetIntegerAsync(unitOfWork, SettingKeys.IdempotencyRetentionDays, SettingTarget.Installation, Ct);
        await After("ISettingsReader.GetIntegerAsync");

        IAccessControl access = services.GetRequiredService<IAccessControl>();
        Result<Principal> principal = await access.ResolvePrincipalAsync(unitOfWork, new AuthenticatedSubject(TestUsers.ClerkSubject), Ct);
        await After("IAccessControl.ResolvePrincipalAsync");
        await access.AuthorizeAsync(unitOfWork, principal.Value, "ITEM.CREATE", RequestScope.ForCompany(Company), TimeProvider.System.GetUtcNow(), Ct);
        await After("IAccessControl.AuthorizeAsync");

        await services.GetRequiredService<IAccountResolver>().ResolveAsync(unitOfWork, Company, AccountRoles.Inventory, june, new PostingKeys(Code.From("TRADE_GOODS")), Ct);
        await After("IAccountResolver.ResolveAsync");

        await services.GetRequiredService<INumberSeriesService>().TakeAsync(unitOfWork, Company, [new NumberRequest(DocumentTypes.InventoryAdjustment)], TimeProvider.System.GetUtcNow(), principal.Value.UserId, Ct);
        await After("INumberSeriesService.TakeAsync");

        // IPostingService resets through the same helper; the posting tests prove it posts as before.
        Assert.All(seen, call => Assert.Equal(("themis_app", call.Call), (call.User, call.Call)));
        Assert.Null(unitOfWork.CurrentRole);
        await unitOfWork.RollbackAsync(Ct);
    }

    [Fact]
    public async Task RR_62_a_D_41_serialization_failure_is_still_retried_once_and_the_change_lands_once()
    {
        Guid counter = await CreateCounterAsync("rr62-retry-create");
        long retriesBefore = _host.Retries;

        CommandReply reply = await _host.PostAsync($"{Counters}/{counter}/touch", new { increment = 3, failure = "SerializationFailureOnFirstAttempt" }, "rr62-retry", ifMatch: "\"1\"", cancellationToken: Ct);

        Assert.True(reply.Status == 200, reply.Body);
        Assert.Equal(1L, _host.Retries - retriesBefore);
        Assert.Equal(8L, await _host.ScalarAsync("SELECT value FROM fixture.counter WHERE id = @id", Ct, ("id", counter)));
        Assert.Equal(1L, await _host.ScalarAsync("SELECT count(*) FROM kernel.outbox_event WHERE event_type = 'FIXTURE_COUNTER_TOUCHED'", Ct));
    }

    [Fact]
    public async Task RR_62_an_RR_45_unique_violation_inside_the_handler_is_still_the_mapped_rejection_and_stored()
    {
        TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int holds = 0;
        _host.ItemCreateSeam.AfterDuplicateCheck = async (number, cancellationToken) =>
        {
            if (number.Value == "RR62-RACE" && Interlocked.Exchange(ref holds, 1) == 0)
            {
                paused.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
        };

        Task<CommandReply> loser = _host.PostAsync(Items, ItemBody("RR62-RACE"), "rr62-loser", cancellationToken: Ct);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(15), Ct);
        CommandReply winner = await _host.PostAsync(Items, ItemBody("RR62-RACE"), "rr62-winner", cancellationToken: Ct);
        Assert.True(winner.Status == 200, winner.Body);
        release.SetResult();
        CommandReply lost = await loser;

        Assert.Equal(422, lost.Status);
        Assert.Equal(("R-MD-05", "item_number_duplicate"), (lost.RuleId, lost.Code));
        CommandReply replay = await _host.PostAsync(Items, ItemBody("RR62-RACE"), "rr62-loser", cancellationToken: Ct);
        Assert.True(replay.Replayed);
        Assert.Equal("item_number_duplicate", replay.Code);
    }

    private async Task<Guid> CreateCounterAsync(string key)
    {
        CommandReply reply = await _host.PostAsync(Counters, new { companyId = Company, initialValue = 5, sample = "1.5" }, key, cancellationToken: Ct);
        Assert.True(reply.Status == 200, reply.Body);
        return reply.Json!["id"]!.GetValue<Guid>();
    }

    private static object ItemBody(string number) =>
        new { companyId = Company, itemNumber = number, description = $"Item {number}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" };
}

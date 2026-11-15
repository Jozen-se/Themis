using System.Reflection;
using System.Runtime.CompilerServices;
using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Identity;
using Themis.Kernel.Organisation;
using Themis.Kernel.Persistence.UnitOfWork;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;

namespace Themis.Integration.Tests.Identity;

/// <summary>
/// D-70, RR-52 (milestone 2 step 4, planner's condition on the installation-scope marking): a permission is marked
/// installation-scope in the catalogue exactly when one of its requests declares installation scope, so the cross-company
/// segregation-of-duties check sees every permission a company-wide assignment in any company can use installation-wide.
/// The declared scope is read the way the pipeline reads it: from the request (IScopedRequest) or from its scope resolver
/// on the fixture's own records, with a settings request at the installation level as one of the samples.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class PermissionScopeMarkingTests : IAsyncLifetime
{
    private readonly PostgresFixture _db;
    private PipelineTestHost _host = null!;

    public PermissionScopeMarkingTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _db.TruncateAsync(Ct);
        _host = await PipelineTestHost.StartAsync(_db);
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task D_70_RR_52_a_permission_is_marked_installation_scope_exactly_when_one_of_its_requests_declares_installation_scope()
    {
        Guid draft = await DraftAsync();
        List<Type> requests = [.. ProductionRequestTypes()];
        Assert.True(requests.Count >= 30, $"Only {requests.Count} request types found.");

        Dictionary<string, bool> declared = new(StringComparer.Ordinal);
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_db.ConnectionStringFor("themis_app"));
        foreach (Type type in requests)
        {
            string permission = type.GetCustomAttribute<CommandAttribute>(inherit: false)?.PermissionCode ?? type.GetCustomAttribute<QueryAttribute>(inherit: false)!.PermissionCode;
            bool installation = false;
            foreach (object sample in Samples(type, draft))
            {
                await using NpgsqlUnitOfWork unitOfWork = await NpgsqlUnitOfWork.BeginAsync(dataSource, Ct);
                RequestScope scope = await ScopeAsync(type, sample, unitOfWork);
                installation |= scope.Kind == ScopeKind.Installation;
            }

            declared[permission] = declared.TryGetValue(permission, out bool earlier) ? earlier || installation : installation;
        }

        foreach (PermissionDefinition permission in PermissionCatalog.Definition.Permissions)
        {
            Assert.True(declared.TryGetValue(permission.Code, out bool installation), $"{permission.Code} has no request type.");
            Assert.True(installation == permission.InstallationScope,
                $"{permission.Code} is marked installation-scope {permission.InstallationScope}, but its requests declare installation scope: {installation} (D-70, RR-52).");
        }
    }

    /// <summary>The scope the pipeline would use: the request's scope resolver when one is registered, else its own declaration.</summary>
    private async Task<RequestScope> ScopeAsync(Type type, object sample, IUnitOfWork unitOfWork)
    {
        Type resolverType = typeof(IScopeResolver<>).MakeGenericType(type);
        if (_host.Services.GetService(resolverType) is { } resolver)
        {
            MethodInfo resolve = resolverType.GetMethod(nameof(IScopeResolver<object>.ResolveAsync))!;
            Task task = (Task)resolve.Invoke(resolver, [sample, unitOfWork, Ct])!;
            await task;
            Result<RequestScope> result = (Result<RequestScope>)task.GetType().GetProperty("Result")!.GetValue(task)!;
            Assert.True(result.IsSuccess, $"{type.Name}: {result.Primary?.Message}");
            return result.Value;
        }

        Assert.True(sample is IScopedRequest, $"{type.Name} has neither a scope resolver nor a declared scope.");
        return ((IScopedRequest)sample).Scope;
    }

    /// <summary>
    /// Samples of a request type built without a constructor: the ids that decide the scope name the fixture's records
    /// (the record the type's name refers to), and a settings request is sampled at the installation and the company level.
    /// </summary>
    private static IEnumerable<object> Samples(Type type, Guid draft)
    {
        Guid id = type.Name switch
        {
            var name when name.Contains("InventoryAdjustment", StringComparison.Ordinal) => draft,
            var name when name.Contains("ChartOfAccounts", StringComparison.Ordinal) || name.Contains("ChartsOfAccounts", StringComparison.Ordinal) => Milestone1OrganisationFixture.ChartOfAccountsId,
            var name when name.Contains("Account", StringComparison.Ordinal) => Milestone1GeneralLedgerFixture.AdjustmentAccountId,
            var name when name.Contains("Warehouse", StringComparison.Ordinal) => Milestone1OrganisationFixture.WarehouseId,
            var name when name.Contains("Site", StringComparison.Ordinal) => Milestone1OrganisationFixture.SiteId,
            _ => Milestone1OrganisationFixture.CompanyId,
        };

        if (type.Name.Contains("Setting", StringComparison.Ordinal))
        {
            yield return Build(type, id, SettingLevel.Installation, company: null);
            yield return Build(type, id, SettingLevel.Company, Milestone1OrganisationFixture.CompanyId);
            yield break;
        }

        yield return Build(type, id, level: null, Milestone1OrganisationFixture.CompanyId);
    }

    private static object Build(Type type, Guid id, SettingLevel? level, Guid? company)
    {
        object sample = RuntimeHelpers.GetUninitializedObject(type);
        Set(sample, "Id", id);
        Set(sample, "CompanyId", company);
        Set(sample, "SiteId", Milestone1OrganisationFixture.SiteId);
        Set(sample, "ChartId", Milestone1OrganisationFixture.ChartOfAccountsId);
        Set(sample, "Key", "company.rounding_mode");
        if (level is { } settingLevel)
        {
            Set(sample, "Level", settingLevel);
            // A settings read names its target, not a level; only the installation sample leaves the company empty.
            Set(sample, "SiteId", (Guid?)null);
        }

        return sample;
    }

    private static void Set(object sample, string name, object? value)
    {
        PropertyInfo? property = sample.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property?.SetMethod is null)
        {
            return;
        }

        Type target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (value is null ? Nullable.GetUnderlyingType(property.PropertyType) is not null || !property.PropertyType.IsValueType : target.IsInstanceOfType(value))
        {
            property.SetValue(sample, value);
        }
    }

    private static IEnumerable<Type> ProductionRequestTypes()
    {
        // Every kernel capability next to the test assembly is loaded, whichever test class ran first.
        foreach (string path in Directory.GetFiles(AppContext.BaseDirectory, "Themis.Kernel.*.dll"))
        {
            Assembly.Load(AssemblyName.GetAssemblyName(path));
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name is { } name && name.StartsWith("Themis.", StringComparison.Ordinal) && !name.Contains("Tests", StringComparison.Ordinal) && !name.Contains("FixtureModule", StringComparison.Ordinal))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<CommandAttribute>(inherit: false) is not null || type.GetCustomAttribute<QueryAttribute>(inherit: false) is not null);
    }

    private async Task<Guid> DraftAsync()
    {
        CommandReply draft = await _host.PostAsync("/v1/kernel/inventory-adjustments", new { companyId = Milestone1OrganisationFixture.CompanyId, documentDate = "2026-06-15", reasonCode = "CountDifference", lines = Array.Empty<object>() }, "scope-draft", subject: TestUsers.ClerkSubject, cancellationToken: Ct);
        Assert.True(draft.Status == 200, draft.Body);
        return draft.Json!["id"]!.GetValue<Guid>();
    }
}

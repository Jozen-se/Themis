using System.Reflection;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Identity;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Identity;

namespace Themis.Kernel.Tests.Identity;

/// <summary>
/// The command catalogue and the permission catalogue hold each other in both directions (step 4 brief, acceptance 2):
/// every command and query type of the production assemblies names a permission in the catalogue, and every
/// permission in the catalogue has its type, or is listed as awaiting the milestone step that builds it.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PermissionCatalogTests
{
    private static IEnumerable<Type> ProductionRequestTypes()
    {
        // The host references every kernel capability; loading its references brings the production assemblies in,
        // whichever test class ran first.
        foreach (AssemblyName reference in typeof(Themis.Host.Program).Assembly.GetReferencedAssemblies().Where(name => name.Name is { } n && n.StartsWith("Themis.", StringComparison.Ordinal)))
        {
            Assembly.Load(reference);
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name is { } name && name.StartsWith("Themis.", StringComparison.Ordinal) && !name.Contains("Tests", StringComparison.Ordinal) && !name.Contains("FixtureModule", StringComparison.Ordinal))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetCustomAttribute<CommandAttribute>(inherit: false) is not null || type.GetCustomAttribute<QueryAttribute>(inherit: false) is not null);
    }

    private static string PermissionOf(Type type) =>
        type.GetCustomAttribute<CommandAttribute>(inherit: false)?.PermissionCode ?? type.GetCustomAttribute<QueryAttribute>(inherit: false)!.PermissionCode;

    [Fact]
    public void Every_registered_command_and_query_names_a_permission_in_the_catalogue()
    {
        List<Type> types = [.. ProductionRequestTypes()];

        Assert.NotEmpty(types);
        Assert.All(types, type => Assert.True(PermissionCatalog.Contains(PermissionOf(type)), $"{type.FullName} names {PermissionOf(type)}, which the permission catalogue lacks."));
    }

    [Fact]
    public void Every_permission_in_the_catalogue_has_its_command_or_query_or_awaits_a_named_step()
    {
        HashSet<string> registered = [.. ProductionRequestTypes().Select(PermissionOf)];

        foreach (PermissionDefinition permission in PermissionCatalog.Definition.Permissions)
        {
            bool hasType = registered.Contains(permission.Code);
            bool awaiting = PermissionCatalog.AwaitingCommands.ContainsKey(permission.Code);
            Assert.True(hasType || awaiting, $"{permission.Code} has no command or query and is not listed as awaiting a step.");
            Assert.False(hasType && awaiting, $"{permission.Code} has its command or query now; remove it from the awaiting list.");
        }
    }

    [Fact]
    public void The_catalogue_is_well_formed_and_the_v1_conflict_pairs_are_the_specification_examples()
    {
        Assert.All(PermissionCatalog.Definition.Permissions, permission => Assert.True(PermissionCodes.IsValid(permission.Code)));
        Assert.Equal(PermissionCatalog.Definition.Permissions.Count, PermissionCatalog.Definition.Permissions.Select(p => p.Code).Distinct(StringComparer.Ordinal).Count());
        // Step 4 brief: INVENTORY_ADJUSTMENT.POST is Sensitive; milestone 2 step 2: SETTING.SET and SETTING.CLEAR are too; milestone 2
        // step 3: COMPANY.CREATE, COMPANY.UPDATE and the chart of accounts header commands; the rest Normal.
        string[] sensitive = ["INVENTORY_ADJUSTMENT.POST", "SETTING.SET", "SETTING.CLEAR", "COMPANY.CREATE", "COMPANY.UPDATE", "CHART_OF_ACCOUNTS.CREATE", "CHART_OF_ACCOUNTS.UPDATE"];
        Assert.All(PermissionCatalog.Definition.Permissions, p => Assert.Equal(sensitive.Contains(p.Code) ? PermissionRiskLevel.Sensitive : PermissionRiskLevel.Normal, p.RiskLevel));

        Assert.Equal(
            [("ACCOUNT.MAINTAIN", "MANUAL_JOURNAL.POST"), ("CUSTOMER.SET_CREDIT_LIMIT", "SALES_ORDER.APPROVE_CREDIT"), ("FISCAL_PERIOD.REOPEN", "MANUAL_JOURNAL.POST")],
            PermissionCatalog.Definition.Conflicts.Select(pair => (pair.PermissionA, pair.PermissionB)).Order());
        Assert.Equal(PermissionCatalog.Pair("B.X", "A.Y"), PermissionCatalog.Pair("A.Y", "B.X"));
        Assert.Throws<ArgumentException>(() => PermissionCatalog.Pair("A.Y", "A.Y"));
    }

    [Fact]
    public void Permission_codes_are_TYPE_VERB_and_a_query_permission_is_TYPE_READ()
    {
        Assert.True(PermissionCodes.IsValid("ITEM.CREATE"));
        Assert.False(PermissionCodes.IsValid("item.create"));
        Assert.False(PermissionCodes.IsValid("ITEM"));
        Assert.Equal("COMPANY.READ", PermissionCodes.Read("COMPANY"));
        Assert.Equal(("INVENTORY_ADJUSTMENT", "POST"), PermissionCodes.Split("INVENTORY_ADJUSTMENT.POST"));
    }
}

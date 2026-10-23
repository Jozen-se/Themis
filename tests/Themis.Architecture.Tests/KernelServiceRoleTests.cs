using System.Reflection;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;

namespace Themis.Architecture.Tests;

/// <summary>
/// RR-62: every kernel Contracts service that runs statements on the caller's unit of work hands the connection back
/// as themis_app through the one internal helper. The catalogue below is exact, so a Contracts interface added later
/// that takes a unit of work fails here until it is classified; the integration test RolePerStatementTests proves
/// the reset on a real connection.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class KernelServiceRoleTests : IClassFixture<ArchitectureFixture>
{
    /// <summary>The services that reset on successful return (RR-62).</summary>
    private static readonly Type[] Resetting =
    [
        typeof(IOrganisationReader), typeof(ISettingsReader), typeof(IPostingService),
        typeof(INumberSeriesService), typeof(IAccountResolver), typeof(IAccessControl),
    ];

    /// <summary>Excluded by the brief: only the pipeline calls them, between its own reset points.</summary>
    private static readonly Type[] PipelineStores = [typeof(IIdempotencyStore), typeof(IAuditStore), typeof(IOutboxStore)];

    /// <summary>Implemented by modules, called by the pipeline after its reset.</summary>
    private static readonly Type[] ModuleImplemented = [typeof(ICommandHandler<,>), typeof(IScopeResolver<>)];

    private readonly ArchitectureFixture _architecture;

    public KernelServiceRoleTests(ArchitectureFixture architecture)
    {
        _architecture = architecture;
    }

    [Fact]
    public void RR_62_every_Contracts_interface_that_takes_a_unit_of_work_is_classified()
    {
        Type[] takesUnitOfWork = [.. typeof(IUnitOfWork).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.GetMethods().Any(method => method.GetParameters().Any(parameter => TakesUnitOfWork(parameter.ParameterType))))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];
        Type[] classified = [.. Resetting.Concat(PipelineStores).Concat(ModuleImplemented).OrderBy(type => type.FullName, StringComparer.Ordinal)];

        Assert.Equal(classified, takesUnitOfWork);
    }

    [Fact]
    public void RR_62_every_implementation_of_a_resetting_service_calls_the_shared_helper()
    {
        // DenyAllAccessControl, the pipeline's fail-closed default, runs no statement and so changes no role.
        Type[] implementations = [.. _architecture.ProductionAssemblyNames
            .Select(Assembly.Load)
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && Resetting.Any(service => service.IsAssignableFrom(type)) && type.Name != "DenyAllAccessControl")];

        Assert.Equal(Resetting.Length, implementations.Length);
        Assert.Empty(implementations.Where(type => !CallsHelper(type)).Select(type => type.FullName));
    }

    [Fact]
    public void RR_62_the_helper_scan_finds_a_call_and_reports_a_type_without_one()
    {
        Assert.True(CallsHelper(Type.GetType("Themis.Kernel.Organisation.OrganisationReader, Themis.Kernel.Organisation", throwOnError: true)!));
        Assert.False(CallsHelper(typeof(KernelServiceRoleTests)));
    }

    private static bool TakesUnitOfWork(Type parameter) =>
        typeof(IUnitOfWork).IsAssignableFrom(parameter) || typeof(ICommandContext).IsAssignableFrom(parameter);

    /// <summary>
    /// Whether the type, or a compiler-generated type nested in it (an async state machine, a closure), calls a method
    /// of KernelServiceRole: every call and callvirt operand in the IL is resolved and its declaring type compared.
    /// </summary>
    private static bool CallsHelper(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<Type> scope = [type, .. type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)];
        foreach (Type candidate in scope)
        {
            foreach (MethodBase method in candidate.GetMethods(All).Cast<MethodBase>().Concat(candidate.GetConstructors(All)))
            {
                byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
                for (int i = 0; il is not null && i + 4 < il.Length; i++)
                {
                    if (il[i] is not (0x28 or 0x6F))
                    {
                        continue;
                    }

                    try
                    {
                        MethodBase? target = candidate.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), candidate.IsGenericType ? candidate.GetGenericArguments() : null, method.IsGenericMethod ? method.GetGenericArguments() : null);
                        if (target?.DeclaringType?.FullName == "Themis.Kernel.Contracts.Persistence.KernelServiceRole")
                        {
                            return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Not a method token: the byte was an operand of another instruction.
                    }
                }
            }
        }

        return false;
    }
}

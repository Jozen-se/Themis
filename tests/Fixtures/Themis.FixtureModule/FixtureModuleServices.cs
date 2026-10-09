using Microsoft.Extensions.DependencyInjection;
using Themis.FixtureModule.Api;
using Themis.FixtureModule.Application;
using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Infrastructure;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;

namespace Themis.FixtureModule;

/// <summary>
/// The module's registration, the one place that sees every layer (the composition entry a host calls; the layer
/// rules ARCH-05 apply to the five layer namespaces, not to this root). The host never calls it: the fixture module
/// is wired by the integration tests only.
/// </summary>
internal static class FixtureModuleServices
{
    public static IServiceCollection AddFixtureModule(this IServiceCollection services)
    {
        services.AddSingleton<FixtureFailureInjector>();
        services.AddSingleton<ICounterStore, SqlCounterStore>();
        services.AddScoped<ICommandHandler<CreateCounter, CounterSnapshot>, CreateCounterHandler>();
        services.AddScoped<ICommandHandler<TouchCounter, CounterSnapshot>, TouchCounterHandler>();
        services.AddSingleton<ICommandValidator<CreateCounter>, CreateCounterValidator>();
        return services;
    }

    /// <summary>Registers the header-based test identity; call before the kernel pipeline so its deny-all defaults yield (R-SEC-01).</summary>
    public static IServiceCollection AddFixtureTestAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPrincipal, HeaderPrincipal>();
        services.AddSingleton<IPermissionEvaluator, HeaderPermissionEvaluator>();
        return services;
    }
}

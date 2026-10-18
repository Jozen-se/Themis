using Microsoft.Extensions.DependencyInjection;
using Themis.FixtureModule.Application;
using Themis.FixtureModule.Contracts;
using Themis.FixtureModule.Infrastructure;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Security;

namespace Themis.FixtureModule;

/// <summary>
/// The module's registration, the one place that sees every layer (the composition entry a host calls; the layer
/// rules ARCH-05 apply to the five layer namespaces, not to this root). The host never calls it: the fixture module
/// is wired by the integration tests only. Identity is the real one from step 4: the tests sign their requests with
/// tokens from the test issuer and the users of the milestone 1 fixture.
/// </summary>
internal static class FixtureModuleServices
{
    public static IServiceCollection AddFixtureModule(this IServiceCollection services)
    {
        services.AddSingleton<FixtureFailureInjector>();
        services.AddSingleton<ICounterStore, SqlCounterStore>();
        services.AddScoped<ICommandHandler<CreateCounter, CounterSnapshot>, CreateCounterHandler>();
        services.AddScoped<ICommandHandler<TouchCounter, CounterSnapshot>, TouchCounterHandler>();
        // Build Brief, Authorisation: a command on an existing record takes its scope from the record.
        services.AddScoped<IScopeResolver<TouchCounter>, CounterScopeResolver>();
        services.AddSingleton<ICommandValidator<CreateCounter>, CreateCounterValidator>();
        return services;
    }
}

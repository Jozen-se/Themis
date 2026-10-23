using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Settings;

/// <summary>Registers the settings reader, SETTING.SET, SETTING.CLEAR and SETTING.READ. Called by the composition root only.</summary>
internal static class KernelSettingsServices
{
    public static IServiceCollection AddKernelSettings(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        // The test seam for a history-bearing definition; the integration tests register theirs first (docs/development/test-seams.md).
        services.TryAddSingleton<ISettingDefinitionSeam, NoSettingDefinitionSeam>();
        services.TryAddSingleton<ISettingWriteSeam, NoSettingWriteSeam>();
        services.AddSingleton<SettingDefinitions>();
        services.AddSingleton<SettingsReader>();
        services.AddSingleton<ISettingsReader>(provider => provider.GetRequiredService<SettingsReader>());
        services.AddScoped<ICommandHandler<SetSetting, SettingValueView>, SetSettingHandler>();
        services.AddScoped<ICommandHandler<ClearSetting, SettingValueView>, ClearSettingHandler>();
        services.AddSingleton<ICommandValidator<SetSetting>, SetSettingValidator>();
        services.AddSingleton<ICommandValidator<ClearSetting>, ClearSettingValidator>();
        // R-API-02: a write that loses the race on the slot's version is the same 412 as a stale If-Match, never stored (RR-45).
        services.AddSingleton(new UniqueConstraintRule(SettingWrites.SlotVersionIndex, "R-API-02", SettingWrites.VersionStaleCode, "The setting changed since the version was read.", ErrorKind.PreconditionFailed));
        services.AddScoped<IQueryHandler<GetSetting, SettingReadView>, GetSettingHandler>();
        // D-63, RR-102: one resolver for every level, inside the transaction; a warehouse declares its company and site.
        services.AddSingleton<SettingScopeResolver>();
        services.AddSingleton<IScopeResolver<SetSetting>>(provider => provider.GetRequiredService<SettingScopeResolver>());
        services.AddSingleton<IScopeResolver<ClearSetting>>(provider => provider.GetRequiredService<SettingScopeResolver>());
        services.AddSingleton<IScopeResolver<GetSetting>>(provider => provider.GetRequiredService<SettingScopeResolver>());
        return services;
    }
}

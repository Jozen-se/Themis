using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Themis.Kernel.Messaging;

/// <summary>
/// Registers the outbox relay's pass, its options from <c>Outbox:Relay</c> (validated at host start, enabled or
/// not) and its telemetry. Called by the composition root only; the host adds the background loop that runs the
/// pass, and the integration tests call the pass directly. The tests register their own <see cref="IOutboxRelaySeam"/>
/// first (Build Brief, Test seams).
/// </summary>
internal static class KernelMessagingServices
{
    public static IServiceCollection AddKernelMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<OutboxRelayOptions>().Bind(configuration.GetSection(OutboxRelayOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<OutboxRelayOptions>, OutboxRelayOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOutboxRelaySeam, NoOutboxRelaySeam>();
        services.AddSingleton<OutboxRelayTelemetry>();
        services.AddSingleton<OutboxRelayPass>();
        services.AddSingleton<IOutboxRelay>(provider => provider.GetRequiredService<OutboxRelayPass>());
        return services;
    }
}

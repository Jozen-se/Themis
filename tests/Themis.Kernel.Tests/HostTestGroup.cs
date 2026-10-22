namespace Themis.Kernel.Tests;

/// <summary>
/// The test classes that start Themis.Host through <c>WebApplicationFactory&lt;Program&gt;</c> run one after another,
/// never in parallel. With the minimal hosting model the factory finds the host it builds through a process-wide
/// diagnostic listener, so two factories starting at the same moment can pick up each other's host, and a test that
/// expects the host to fail on its options then sees an ObjectDisposedException from the other host's provider.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostTestGroup
{
    public const string Name = "Themis.Host";
}

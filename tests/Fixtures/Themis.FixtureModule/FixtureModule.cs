// The fixture module's five layers as namespaces in one test-only assembly. Each holds the smallest type that lets
// the architecture tests see the layer. Contracts is the module's only public surface (ARCH-07).

namespace Themis.FixtureModule.Contracts
{
    /// <summary>The fixture module's public contract surface.</summary>
    public static class FixtureContracts
    {
        public const string SchemaName = "fixture";
        public const string RoleName = "themis_fixture";
    }
}

namespace Themis.FixtureModule.Domain
{
    internal static class FixtureDomain
    {
    }
}

namespace Themis.FixtureModule.Application
{
    internal static class FixtureApplication
    {
        public static string Schema => Themis.FixtureModule.Contracts.FixtureContracts.SchemaName;
    }
}

namespace Themis.FixtureModule.Infrastructure
{
    internal static class FixtureInfrastructure
    {
    }
}

namespace Themis.FixtureModule.Api
{
    internal static class FixtureApi
    {
    }
}

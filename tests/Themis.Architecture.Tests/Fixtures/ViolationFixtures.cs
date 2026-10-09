// Deliberate violations of the boundary rules, compiled into the test assembly only. They are never loaded into the
// production architecture (ArchitectureFixture excludes test assemblies) and exist so that every rule has a negative
// test. Each type names the rule it violates; the "allowed" types prove the rules do not over-match.

#pragma warning disable CA1812 // instantiated by nobody on purpose
#pragma warning disable CA1822
#pragma warning disable IDE0060

namespace Elsewhere
{
    /// <summary>ARCH-01: a type outside the namespace of its assembly.</summary>
    internal static class StrayType
    {
    }
}

namespace Themis.Host
{
    internal static class HostFixtureType
    {
    }
}

namespace Themis.Migrator
{
    internal static class MigratorFixtureType
    {
    }
}

namespace Themis.Kernel.Primitives
{
    public sealed class FixturePrimitive
    {
    }

    /// <summary>ARCH-06: primitives must not depend on a web framework.</summary>
    internal sealed class UsesWebFramework
    {
        public Microsoft.AspNetCore.Http.HttpContext? Context { get; set; }
    }

    /// <summary>ARCH-08: primitives depend on nothing else in Themis.</summary>
    internal sealed class UsesContracts
    {
        public Themis.Kernel.Contracts.KernelCommand? Command { get; set; }
    }
}

namespace Themis.Kernel.Contracts
{
    public sealed class KernelCommand
    {
    }

    /// <summary>ARCH-08: Contracts must not depend on a kernel capability.</summary>
    internal sealed class ContractUsingCapability
    {
        public Themis.Kernel.Posting.PostingService? Service { get; set; }
    }
}

namespace Themis.Kernel.Posting
{
    internal sealed class PostingService
    {
    }

    /// <summary>ARCH-08: a capability must not depend on another capability.</summary>
    internal sealed class UsesOtherCapability
    {
        public Themis.Kernel.Documents.DocumentEngine? Engine { get; set; }
    }

    /// <summary>Allowed: a capability depends on Contracts and Primitives.</summary>
    internal sealed class UsesContracts
    {
        public Themis.Kernel.Contracts.KernelCommand? Command { get; set; }

        public Themis.Kernel.Primitives.FixturePrimitive? Primitive { get; set; }
    }
}

namespace Themis.Kernel.Documents
{
    internal sealed class DocumentEngine
    {
    }

    /// <summary>ARCH-02: kernel code must not depend on a module.</summary>
    internal sealed class KernelUsingModule
    {
        public Themis.Modules.Beta.Contracts.BetaCommand? Command { get; set; }
    }
}

namespace Themis.Kernel.Pipeline
{
    /// <summary>ARCH-07: no public types outside Primitives and Contracts.</summary>
    public sealed class PublicLeak
    {
    }

    /// <summary>ARCH-02: kernel code must not depend on the migrator.</summary>
    internal sealed class UsesMigrator
    {
        public Type MigratorType => typeof(Themis.Migrator.MigratorFixtureType);
    }
}

namespace Themis.Modules.Alpha.Domain
{
    /// <summary>ARCH-04: a module must not depend on a kernel capability.</summary>
    internal sealed class UsesKernelInternals
    {
        public Themis.Kernel.Posting.PostingService? Service { get; set; }
    }
}

namespace Themis.Modules.Alpha.Application
{
    /// <summary>ARCH-04: a module must not depend on another module's internals.</summary>
    internal sealed class UsesOtherModuleInternals
    {
        public Themis.Modules.Beta.Domain.BetaEntity? Entity { get; set; }
    }

    /// <summary>Allowed: a module may depend on another module's Contracts.</summary>
    internal sealed class UsesOtherModuleContracts
    {
        public Themis.Modules.Beta.Contracts.BetaCommand? Command { get; set; }
    }

    /// <summary>Allowed: a module may depend on the kernel primitives and contracts.</summary>
    internal sealed class UsesKernelPrimitives
    {
        public Themis.Kernel.Primitives.FixturePrimitive? Primitive { get; set; }

        public Themis.Kernel.Contracts.KernelCommand? Command { get; set; }
    }
}

namespace Themis.Modules.Beta.Contracts
{
    public sealed class BetaCommand
    {
    }

    /// <summary>ARCH-05: Contracts must not depend on Domain.</summary>
    internal sealed class ContractUsingDomain
    {
        public Themis.Modules.Beta.Domain.BetaEntity? Entity { get; set; }
    }
}

namespace Themis.Modules.Beta.Domain
{
    internal sealed class BetaEntity
    {
    }
}

namespace Themis.Modules.Beta.Application
{
    /// <summary>ARCH-05: Application must not depend on Infrastructure.</summary>
    internal sealed class UsesOwnInfrastructure
    {
        public Themis.Modules.Beta.Infrastructure.BetaRepository? Repository { get; set; }
    }

    /// <summary>Allowed: Application depends on its own Domain.</summary>
    internal sealed class UsesOwnDomain
    {
        public Themis.Modules.Beta.Domain.BetaEntity? Entity { get; set; }
    }

    /// <summary>ARCH-03: nothing depends on the host.</summary>
    internal sealed class UsesHost
    {
        public Type HostType => typeof(Themis.Host.HostFixtureType);
    }
}

namespace Themis.Modules.Beta.Infrastructure
{
    internal sealed class BetaRepository
    {
    }
}

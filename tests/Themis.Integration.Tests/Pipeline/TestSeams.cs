using Themis.FixtureModule.Application;
using Themis.FixtureModule.Contracts;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.MasterData;
using Themis.Kernel.Messaging;
using Themis.Kernel.Organisation;
using Themis.Kernel.Posting;
using Themis.Kernel.Primitives;
using Themis.Kernel.Settings;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// The test implementations of the kernel's seams (Build Brief, Test seams; docs/development/test-seams.md). They
/// live in the test project, which sees the kernel's internals, and <see cref="PipelineTestHost"/> registers them
/// before the kernel's registrations, which keep them (TryAdd). The production host never references them.
/// </summary>
internal sealed class PostingFailureSwitch
{
    /// <summary>What the posting hook raises after the ledger writes; a test sets it before the command and resets it after.</summary>
    public FixtureFailure Failure { get; set; }

    /// <summary>Runs on the command's unit of work after the ledger writes, for the RR-47 test that plants an unbalanced journal line and lets the deferred trigger fail the commit.</summary>
    public Func<IUnitOfWork, Guid, CancellationToken, Task>? AfterLedgerWrites { get; set; }
}

/// <summary>
/// The posting hook of the step 8 tests: after the ledger writes and before the document update it raises the
/// failure the <see cref="PostingFailureSwitch"/> names, through the fixture module's injector, so the tests prove
/// that a failure after the inventory entries leaves nothing behind (R-LED-05, I-09) and that a serialization failure
/// on the first attempt is retried once (D-41). Only ThrowAfterWrite and SerializationFailureOnFirstAttempt apply.
/// </summary>
internal sealed class TestPostingHook : IPostingHook
{
    private readonly PostingFailureSwitch _switch;
    private readonly FixtureFailureInjector _failures;

    public TestPostingHook(PostingFailureSwitch failureSwitch, FixtureFailureInjector failures)
    {
        _switch = failureSwitch;
        _failures = failures;
    }

    public async Task AfterLedgerWritesAsync(IUnitOfWork unitOfWork, Guid documentId, ICommandContext context, CancellationToken cancellationToken)
    {
        if (_switch.AfterLedgerWrites is { } afterLedgerWrites)
        {
            await afterLedgerWrites(unitOfWork, documentId, cancellationToken);
        }

        FixtureFailure failure = _switch.Failure;
        if (failure is FixtureFailure.ThrowAfterWrite or FixtureFailure.SerializationFailureOnFirstAttempt)
        {
            await _failures.ApplyAsync(new FixtureFailureContext(failure, context.IdempotencyKey.Value, _ => Task.CompletedTask), cancellationToken);
        }
    }
}

/// <summary>
/// The relay seam of the step 9 tests: between the claim and the commit of a pass it runs whatever a test set, to
/// hold the pass while another runs or to make it fail after the claim (D-53). Nothing set means nothing happens.
/// </summary>
internal sealed class TestOutboxRelaySeam : IOutboxRelaySeam
{
    public Func<IReadOnlyList<Guid>, CancellationToken, Task>? AfterClaim { get; set; }

    public Task AfterClaimAsync(IReadOnlyList<Guid> eventIds, CancellationToken cancellationToken) =>
        AfterClaim is { } afterClaim ? afterClaim(eventIds, cancellationToken) : Task.CompletedTask;
}

/// <summary>
/// The item create seam of the RR-45 test: holds the first ITEM.CREATE after its duplicate pre-check while a second
/// create with the same number commits, so the first meets the unique index. Nothing set means nothing happens.
/// </summary>
internal sealed class TestItemCreateSeam : IItemCreateSeam
{
    public Func<Code, CancellationToken, Task>? AfterDuplicateCheck { get; set; }

    public Task AfterDuplicateCheckAsync(Code itemNumber, CancellationToken cancellationToken) =>
        AfterDuplicateCheck is { } afterDuplicateCheck ? afterDuplicateCheck(itemNumber, cancellationToken) : Task.CompletedTask;
}

/// <summary>
/// The definitions beside the catalogue for the settings tests (milestone 2 step 2; docs/development/test-seams.md): a
/// history-bearing definition, since none of the milestone 2 definitions carries valid_from. A test adds its definition
/// here and writes the matching row to kernel.setting_definition as the migrator before the first command.
/// </summary>
internal sealed class TestSettingDefinitionSeam : ISettingDefinitionSeam
{
    private readonly List<SettingDefinition> _definitions = [];

    public IReadOnlyList<SettingDefinition> Definitions => _definitions;

    public void Add(SettingDefinition definition) => _definitions.Add(definition);
}

/// <summary>The pause point of the settings race test (milestone 2 step 2; R-API-02, RR-45): a test sets the delegate; nothing happens otherwise.</summary>
internal sealed class TestSettingWriteSeam : ISettingWriteSeam
{
    public Func<string, SettingLevel, CancellationToken, Task>? AfterVersionCheck { get; set; }

    public Task AfterVersionCheckAsync(string key, SettingLevel level, CancellationToken cancellationToken) =>
        AfterVersionCheck is { } hook ? hook(key, level, cancellationToken) : Task.CompletedTask;
}

/// <summary>
/// The organisation create seam of the step 3 race tests (RR-45): holds the first of two creates with the same code or
/// organisation number after its pre-checks while a second commits, so the first meets the unique index. Nothing set
/// means nothing happens.
/// </summary>
internal sealed class TestOrganisationCreateSeam : IOrganisationCreateSeam
{
    public Func<string, Code, CancellationToken, Task>? AfterDuplicateChecks { get; set; }

    public Task AfterDuplicateChecksAsync(string entityType, Code code, CancellationToken cancellationToken) =>
        AfterDuplicateChecks is { } hook ? hook(entityType, code, cancellationToken) : Task.CompletedTask;
}

/// <summary>
/// The company update seam of the D-65 concurrency tests (milestone 2 step 3): holds COMPANY.UPDATE after its versioned
/// UPDATE, with the company row locked, while a posting starts and waits on its FOR SHARE. Nothing set means nothing happens.
/// </summary>
internal sealed class TestCompanyUpdateSeam : ICompanyUpdateSeam
{
    public Func<Guid, CancellationToken, Task>? AfterVersionedUpdate { get; set; }

    public Task AfterVersionedUpdateAsync(Guid companyId, CancellationToken cancellationToken) =>
        AfterVersionedUpdate is { } hook ? hook(companyId, cancellationToken) : Task.CompletedTask;
}

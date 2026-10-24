using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using CsCheck;
using Npgsql;
using Themis.Integration.Tests.Pipeline;
using Themis.Kernel.Contracts.Security;
using Themis.Kernel.Organisation;

namespace Themis.Integration.Tests.Invariants;

/// <summary>
/// Property-based runs through the real pipeline over HTTP (step 11 brief; D-27): random sequences of ITEM.CREATE and
/// INVENTORY_ADJUSTMENT CREATE, UPDATE, DISCARD and POST with random items, signed quantities, entered costs present
/// or absent, stock statuses, reason codes (Scrap included), posting dates across the Open periods and the Closed
/// period 1, two warehouses, the clerk and the preparer, interleaved with relay passes. A rejected command is a valid
/// outcome; an unhandled failure (500) is not. After every command every invariant query of db/invariants returns no
/// rows (I-01, I-02, I-03, I-04, I-06, I-08, I-09, I-15). Each sequence starts from a fresh database cloned from a
/// migrated and seeded template with a second warehouse. A failing run shrinks to the smallest failing sequence and
/// prints its seed and commands; THEMIS_PROPERTY_SEED reproduces it. The concurrent variant runs two sequences on the
/// same items at once: the invariants hold afterwards and no command retried after a deadlock (40P01), which R-LED-08
/// forbids; on failure it logs the seed and both command lists and does not shrink. THEMIS_PROPERTY_SEQUENCES sets
/// the number of sequences (200 on a pull request; the long run workflow passes more).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Invariant")]
public sealed class InvariantPropertyTests : IAsyncLifetime
{
    private const string Items = "/v1/kernel/items";
    private const string Adjustments = "/v1/kernel/inventory-adjustments";
    private const int DefaultSequences = 200;

    private static readonly Guid Company = Milestone1OrganisationFixture.CompanyId;
    private static readonly Guid MainWarehouse = Milestone1OrganisationFixture.WarehouseId;

    /// <summary>The second warehouse of the property fixture, seeded into the template (test data, docs/development/test-seams.md).</summary>
    private static readonly Guid SecondWarehouse = Guid.Parse("00000000-0000-7000-8000-00000000b002");

    private static readonly int[] AllowedStatuses = [200, 400, 403, 404, 412, 422];

    private readonly PostgresFixture _db;
    private readonly ConcurrentDictionary<string, long> _outcomes = new(StringComparer.Ordinal);
    private string _template = string.Empty;

    public InvariantPropertyTests(PostgresFixture db)
    {
        _db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static int Sequences => Setting("THEMIS_PROPERTY_SEQUENCES", DefaultSequences);

    private static int Threads => Setting("THEMIS_PROPERTY_THREADS", 2);

    /// <summary>How many smaller sequences CsCheck may try to shrink a failing one; the shrunk sequence is the one reported.</summary>
    private static int ShrinkIterations => Setting("THEMIS_PROPERTY_SHRINK_ITERATIONS", 100);

    private static string Seed
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable("THEMIS_PROPERTY_SEED");
            return string.IsNullOrWhiteSpace(configured) ? new PCG((uint)Random.Shared.Next(), (ulong)Random.Shared.NextInt64()).ToString() : configured.Trim();
        }
    }

    public async ValueTask InitializeAsync()
    {
        _template = await _db.CreateSeededDatabaseAsync(Ct);
        string system = WellKnownUsers.System.Value.ToString();
        string migrator = new NpgsqlConnectionStringBuilder(_template) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
        await ExecuteAsync(migrator,
            $"INSERT INTO kernel.warehouse (id, site_id, company_id, code, name, is_default, status, created_at, created_by, updated_at, updated_by) VALUES ('{SecondWarehouse}', '{Milestone1OrganisationFixture.SiteId}', '{Company}', 'PROP-W2', 'Property fixture warehouse 2', false, 'Active', now(), '{system}', now(), '{system}')");
        // RR-74 (milestone 2 step 3): the property runs post under HalfEven, switched on SE01 in the template before its first
        // posting, which R-MD-06 still allows (a company created by command cannot post before steps 5 and 6).
        await ExecuteAsync(migrator, $"UPDATE kernel.company SET rounding_mode = 'HalfEven' WHERE id = '{Company}'");
    }

    public async ValueTask DisposeAsync() => await _db.DropDatabaseAsync(_template, CancellationToken.None);

    // ---- the model -------------------------------------------------------------------------------------------------

    private enum Kind
    {
        CreateItem,
        Create,
        Update,
        Discard,
        Post,
        Relay,
    }

    /// <summary>
    /// A line as generated: the item by its index in the sequence's pool (resolved at run time to a created item, or to
    /// an unknown id when the pool is empty or the line says so), the warehouse as 0 (main), 1 (second) or none (the
    /// header's), the stock status and an optional entered unit cost.
    /// </summary>
    private sealed record LineSpec(int Item, bool UnknownItem, decimal Quantity, int? Warehouse, string StockStatus, decimal? EnteredUnitCost)
    {
        public override string ToString() =>
            $"{(UnknownItem ? "unknown-item" : $"item{Item}")} {Quantity.ToString(CultureInfo.InvariantCulture)}{(Warehouse is null ? string.Empty : $" wh{Warehouse}")}{(StockStatus == "Available" ? string.Empty : " " + StockStatus)}{(EnteredUnitCost is null ? string.Empty : " @" + EnteredUnitCost.Value.ToString(CultureInfo.InvariantCulture))}";
    }

    private sealed record DraftSpec(string? ReasonCode, string? PostingDate, int? Warehouse, IReadOnlyList<LineSpec> Lines)
    {
        public override string ToString() => $"reason={ReasonCode ?? "none"} posting={PostingDate ?? "none"} wh={(Warehouse is null ? "none" : Warehouse.ToString())} lines=[{string.Join(" | ", Lines)}]";
    }

    private sealed record Step(Kind Kind, int Item, int Slot, DraftSpec? Draft, bool StaleVersion, bool AsPreparer)
    {
        public override string ToString() => Kind switch
        {
            Kind.CreateItem => $"ITEM.CREATE item{Item}{(AsPreparer ? " as preparer" : string.Empty)}",
            Kind.Create => $"INVENTORY_ADJUSTMENT.CREATE slot{Slot} {Draft}",
            Kind.Update => $"INVENTORY_ADJUSTMENT.UPDATE slot{Slot}{(StaleVersion ? " stale version" : string.Empty)} {Draft}",
            Kind.Discard => $"INVENTORY_ADJUSTMENT.DISCARD slot{Slot}{(StaleVersion ? " stale version" : string.Empty)}",
            Kind.Post => $"INVENTORY_ADJUSTMENT.POST slot{Slot}{(StaleVersion ? " stale version" : string.Empty)}{(AsPreparer ? " as preparer" : string.Empty)}",
            _ => "RELAY PASS",
        };
    }

    private sealed record Sequence(IReadOnlyList<Step> Steps)
    {
        public override string ToString()
        {
            StringBuilder text = new();
            text.Append(CultureInfo.InvariantCulture, $"{Steps.Count} command(s):");
            for (int i = 0; i < Steps.Count; i++)
            {
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"  {i + 1,3}. {Steps[i]}");
            }

            return text.ToString();
        }
    }

    private static readonly Gen<int> ItemIndex = Gen.Int[0, 3];
    private static readonly Gen<int> SlotIndex = Gen.Int[0, 3];

    private static readonly Gen<decimal> Quantity = Gen.Frequency(
        (8, Gen.Int[1, 60].Select(q => (decimal)q)),
        (3, Gen.Int[1, 60].Select(q => -(decimal)q)),
        (1, Gen.Int[100_000_000, 100_000_000].Select(q => (decimal)q)),
        (1, Gen.Int[1, 9].Select(tenths => tenths / 10m)));

    private static readonly Gen<decimal?> EnteredUnitCost = Gen.Frequency(
        (2, Gen.Const((decimal?)null)),
        (7, Gen.Int[0, 500].Select(c => (decimal?)(c / 100m))),
        (1, Gen.Int[1, 99_999].Select(c => (decimal?)(c / 10_000m))),
        (1, Gen.Const<decimal?>(0m)));

    private static readonly Gen<string> StockStatus = Gen.FrequencyConst((8, "Available"), (2, "Blocked"));

    private static readonly Gen<int?> LineWarehouse = Gen.FrequencyConst((5, (int?)null), (3, 0), (2, 1));

    private static readonly Gen<bool> UnknownItem = Gen.FrequencyConst((19, false), (1, true));

    /// <summary>An outbound line carries an entered cost in one case of ten, which R-INV-05 rejects; the other nine cost at the moving average.</summary>
    private static readonly Gen<bool> CostOnOutbound = Gen.FrequencyConst((9, false), (1, true));

    private static readonly Gen<LineSpec> Line = Gen.Select(ItemIndex, UnknownItem, Quantity, LineWarehouse, StockStatus, EnteredUnitCost, CostOnOutbound,
        (item, unknown, quantity, warehouse, status, cost, costOnOutbound) => new LineSpec(item, unknown, quantity, warehouse, status, quantity < 0 && !costOnOutbound ? null : cost));

    private static readonly Gen<string?> ReasonCode = Gen.FrequencyConst((14, "CountDifference"), (1, "Scrap"), (1, (string?)null), (1, "Other"));

    private static readonly Gen<string?> PostingDate = Gen.Frequency(
        (14, Gen.Int[2, 12].Select(month => (string?)$"2026-{month:00}-15")),
        (1, Gen.Const<string?>("2026-01-15")),
        (1, Gen.Const((string?)null)),
        (1, Gen.Const<string?>("2027-03-01")));

    private static readonly Gen<int?> HeaderWarehouse = Gen.FrequencyConst((6, (int?)0), (3, 1), (1, null));

    private static readonly Gen<DraftSpec> Draft = Gen.Select(ReasonCode, PostingDate, HeaderWarehouse, Line.List[1, 3], (reason, date, warehouse, lines) => new DraftSpec(reason, date, warehouse, lines));

    private static readonly Gen<bool> Stale = Gen.FrequencyConst((9, false), (1, true));
    private static readonly Gen<bool> Preparer = Gen.FrequencyConst((9, false), (1, true));

    private static readonly Gen<Step> StepGen = Gen.Frequency(
        (3, Gen.Select(ItemIndex, Preparer, (item, preparer) => new Step(Kind.CreateItem, item, 0, null, false, preparer))),
        (4, Gen.Select(SlotIndex, Draft, (slot, draft) => new Step(Kind.Create, 0, slot, draft, false, false))),
        (2, Gen.Select(SlotIndex, Draft, Stale, (slot, draft, stale) => new Step(Kind.Update, 0, slot, draft, stale, false))),
        (1, Gen.Select(SlotIndex, Stale, (slot, stale) => new Step(Kind.Discard, 0, slot, null, stale, false))),
        (5, Gen.Select(SlotIndex, Stale, Preparer, (slot, stale, preparer) => new Step(Kind.Post, 0, slot, null, stale, preparer))),
        (2, Gen.Const(new Step(Kind.Relay, 0, 0, null, false, false))));

    /// <summary>A sequence opens with one to three ITEM.CREATE steps and one CREATE, so that the later commands find items and a draft, and goes on with up to 22 random steps.</summary>
    private static readonly Gen<Sequence> SequenceGen = Gen.Select(
        Gen.Select(ItemIndex, Preparer, (item, preparer) => new Step(Kind.CreateItem, item, 0, null, false, preparer)).List[1, 3],
        Gen.Select(SlotIndex, Draft, (slot, draft) => new Step(Kind.Create, 0, slot, draft, false, false)),
        StepGen.List[0, 22],
        (items, draft, steps) => new Sequence([.. items, draft, .. steps]));

    // ---- the properties --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Random_command_sequences_through_the_pipeline_keep_I_01_I_02_I_03_I_04_I_06_I_08_I_09_and_I_15_true_after_every_command()
    {
        string seed = Seed;
        int sequences = Sequences;
        int threads = Threads;
        TestContext.Current.TestOutputHelper?.WriteLine($"THEMIS_PROPERTY_SEED={seed} THEMIS_PROPERTY_SEQUENCES={sequences} THEMIS_PROPERTY_THREADS={threads}");

        // The run is deterministic from its seed: the seed itself is the first sequence, every further sequence has a seed
        // drawn from it. A failing sequence is handed to CsCheck under its own seed, which regenerates it and shrinks it
        // within the shrink budget; the exception names the shrunk sequence's seed, which reproduces it as the first
        // sequence of the next run.
        PCG master = PCG.Parse(seed);
        List<string> caseSeeds = [seed];
        while (caseSeeds.Count < sequences)
        {
            caseSeeds.Add(new PCG(master.Next(), master.Next64()).ToString());
        }

        try
        {
            // A failing sequence stops the loop from starting further ones; its exception, with the seed and the shrunk commands, is the test's failure.
            await Parallel.ForEachAsync(caseSeeds, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = Ct }, async (caseSeed, token) =>
            {
                Sequence sequence = SequenceGen.Generate(PCG.Parse(caseSeed), null, out Size _);
                try
                {
                    await RunSequenceAsync(sequence);
                }
                catch (Exception first) when (first is not OperationCanceledException)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine($"Sequence with seed {caseSeed} failed; shrinking it over up to {ShrinkIterations} smaller sequences: {first.Message}");
                    await Check.SampleAsync(SequenceGen, RunSequenceAsync, seed: caseSeed, iter: ShrinkIterations, threads: 1, print: shrunk => shrunk.ToString());
                    throw;
                }
            });
        }
        finally
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Outcomes per command and status: " + string.Join(", ", _outcomes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}")));
        }

        // The property is not vacuous: postings succeeded, and rejections of every kind the guards produce occurred.
        Assert.True(_outcomes.GetValueOrDefault("Post 200") > 0, "No INVENTORY_ADJUSTMENT.POST succeeded in the run.");
        Assert.True(_outcomes.Any(pair => pair.Key.StartsWith("Post 422 ", StringComparison.Ordinal)), "No INVENTORY_ADJUSTMENT.POST was rejected by a guard in the run.");
    }

    [Fact]
    public async Task R_LED_08_two_random_sequences_on_the_same_items_run_at_once_keep_the_invariants_and_never_retry_after_a_deadlock()
    {
        string seed = Seed;
        int pairs = Math.Max(10, Sequences / 10);
        TestContext.Current.TestOutputHelper?.WriteLine($"THEMIS_PROPERTY_SEED={seed} concurrent pairs={pairs}");
        PCG pcg = PCG.Parse(seed);
        Gen<List<DraftSpec>> listGen = ConcurrentDraft.List[2, 8];
        for (int pair = 1; pair <= pairs; pair++)
        {
            ulong state = pcg.State;
            List<DraftSpec> first = listGen.Generate(pcg, null, out _);
            List<DraftSpec> second = listGen.Generate(pcg, null, out _);
            try
            {
                await RunConcurrentPairAsync(first, second);
            }
            catch (Exception exception)
            {
                // No shrinking: the failure is reported with the seed of this pair and both command lists (step 11 brief).
                string report = $"Concurrent pair {pair} failed (THEMIS_PROPERTY_SEED={pcg.ToString(state)} reproduces it as pair 1).{Environment.NewLine}" +
                                $"First list:{Environment.NewLine}{Describe(first)}{Environment.NewLine}Second list:{Environment.NewLine}{Describe(second)}";
                TestContext.Current.TestOutputHelper?.WriteLine(report);
                throw new InvalidOperationException(report, exception);
            }
        }
    }

    /// <summary>Posted drafts of the concurrent variant: small signed quantities over the shared items, mostly costed inbound and uncosted outbound, Open periods only.</summary>
    private static readonly Gen<DraftSpec> ConcurrentDraft = Gen.Select(
        Gen.Int[2, 12].Select(month => (string?)$"2026-{month:00}-15"),
        Gen.Select(Gen.Int[0, 2], Gen.Frequency((1, Gen.Int[1, 20].Select(q => (decimal)q)), (1, Gen.Int[1, 20].Select(q => -(decimal)q))), Gen.FrequencyConst((7, (decimal?)null), (3, 1.25m)), (item, quantity, cost) => new LineSpec(item, false, quantity, null, "Available", quantity > 0 ? cost : null)).List[1, 3],
        (date, lines) => new DraftSpec("CountDifference", date, 0, lines));

    // ---- running a sequence ----------------------------------------------------------------------------------------

    private sealed class RunState
    {
        public Dictionary<int, Guid> Items { get; } = [];

        public Dictionary<int, (Guid Id, long Version)> Slots { get; } = [];

        /// <summary>The item of a line: the pool item by its index, another created item when that index was not created yet, an unknown id when none exists or the line asks for one.</summary>
        public Guid ItemOf(LineSpec line)
        {
            if (line.UnknownItem || Items.Count == 0)
            {
                return Guid.NewGuid();
            }

            if (Items.TryGetValue(line.Item, out Guid item))
            {
                return item;
            }

            return Items.OrderBy(pair => pair.Key).ElementAt(line.Item % Items.Count).Value;
        }

        /// <summary>The draft a step targets: the slot's draft, another existing draft when the slot is empty, or an unknown id when no draft exists (a 404 is a valid outcome).</summary>
        public (int Slot, Guid Id, long Version)? SlotOf(int slot)
        {
            if (Slots.TryGetValue(slot, out (Guid Id, long Version) exact))
            {
                return (slot, exact.Id, exact.Version);
            }

            if (Slots.Count == 0)
            {
                return null;
            }

            KeyValuePair<int, (Guid Id, long Version)> other = Slots.OrderBy(pair => pair.Key).ElementAt(slot % Slots.Count);
            return (other.Key, other.Value.Id, other.Value.Version);
        }
    }

    private async Task RunSequenceAsync(Sequence sequence)
    {
        string clone = await _db.CloneDatabaseAsync(_template, Ct);
        try
        {
            string database = new NpgsqlConnectionStringBuilder(clone).Database!;
            string migrator = new NpgsqlConnectionStringBuilder(clone) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            await using PipelineTestHost host = await PipelineTestHost.StartAsync(_db, database);
            RunState state = new();
            for (int i = 0; i < sequence.Steps.Count; i++)
            {
                Step step = sequence.Steps[i];
                CommandReply? reply = await ExecuteStepAsync(host, state, step, $"seq-{i + 1}");
                _outcomes.AddOrUpdate($"{step.Kind} {reply?.Status.ToString(CultureInfo.InvariantCulture) ?? "pass"}{(reply?.Code is { } code ? " " + code : string.Empty)}", 1, (_, count) => count + 1);
                if (reply is not null && !AllowedStatuses.Contains(reply.Status))
                {
                    throw new InvalidOperationException($"Command {i + 1} ({step}) answered {reply.Status}: {reply.Body}");
                }

                IReadOnlyList<InvariantViolation> violations = await InvariantQueries.ViolationsAsync(migrator, Ct);
                if (violations.Count > 0)
                {
                    throw new InvalidOperationException($"After command {i + 1} ({step}, status {reply?.Status.ToString(CultureInfo.InvariantCulture) ?? "relay"}) the invariants are violated:{Environment.NewLine}{InvariantQueries.Describe(violations)}");
                }
            }
        }
        finally
        {
            await _db.DropDatabaseAsync(clone, CancellationToken.None);
        }
    }

    private async Task RunConcurrentPairAsync(IReadOnlyList<DraftSpec> first, IReadOnlyList<DraftSpec> second)
    {
        string clone = await _db.CloneDatabaseAsync(_template, Ct);
        try
        {
            string database = new NpgsqlConnectionStringBuilder(clone).Database!;
            string migrator = new NpgsqlConnectionStringBuilder(clone) { Username = "themis_migrator", Password = PostgresFixture.TestRolePassword }.ConnectionString;
            await using PipelineTestHost host = await PipelineTestHost.StartAsync(_db, database);
            RunState state = new();
            // Three shared items with opening stock, so that both lists lock the same cost state and balance rows (R-LED-08).
            for (int item = 0; item < 3; item++)
            {
                CommandReply created = await ExecuteStepAsync(host, state, new Step(Kind.CreateItem, item, 0, null, false, false), $"con-item-{item}") ?? throw new InvalidOperationException("no reply");
                Assert.Equal(200, created.Status);
                DraftSpec opening = new("CountDifference", "2026-02-15", 0, [new LineSpec(item, false, 500m, null, "Available", 2.00m)]);
                CommandReply posted = await PostNewAsync(host, state, opening, 10 + item, $"con-open-{item}");
                Assert.Equal(200, posted.Status);
            }

            long deadlockRetriesBefore = host.RetriesBySqlState.GetValueOrDefault("40P01");
            Task<List<CommandReply>> left = RunListAsync(host, state, first, slotBase: 100, "con-a");
            Task<List<CommandReply>> right = RunListAsync(host, state, second, slotBase: 200, "con-b");
            await Task.WhenAll(left, right);

            foreach (CommandReply reply in left.Result.Concat(right.Result))
            {
                if (!AllowedStatuses.Contains(reply.Status))
                {
                    throw new InvalidOperationException($"A concurrent command answered {reply.Status}: {reply.Body}");
                }
            }

            IReadOnlyList<InvariantViolation> violations = await InvariantQueries.ViolationsAsync(migrator, Ct);
            if (violations.Count > 0)
            {
                throw new InvalidOperationException($"After the concurrent lists the invariants are violated:{Environment.NewLine}{InvariantQueries.Describe(violations)}");
            }

            long deadlockRetries = host.RetriesBySqlState.GetValueOrDefault("40P01") - deadlockRetriesBefore;
            if (deadlockRetries != 0)
            {
                throw new InvalidOperationException($"{deadlockRetries} command retr{(deadlockRetries == 1 ? "y" : "ies")} after a deadlock (40P01); R-LED-08 requires none.");
            }
        }
        finally
        {
            await _db.DropDatabaseAsync(clone, CancellationToken.None);
        }
    }

    private static async Task<List<CommandReply>> RunListAsync(PipelineTestHost host, RunState state, IReadOnlyList<DraftSpec> drafts, int slotBase, string keyPrefix)
    {
        List<CommandReply> replies = [];
        for (int i = 0; i < drafts.Count; i++)
        {
            // The state's item ids are read only; each list has its own slots, so the shared dictionary is never written concurrently.
            replies.Add(await PostNewAsync(host, state, drafts[i], slotBase + i, $"{keyPrefix}-{i + 1}"));
        }

        return replies;
    }

    private static async Task<CommandReply> PostNewAsync(PipelineTestHost host, RunState state, DraftSpec draft, int slot, string key)
    {
        CommandReply created = await host.PostAsync(Adjustments, DraftBody(state, draft), key + "-create", cancellationToken: Ct);
        if (created.Status != 200)
        {
            return created;
        }

        Guid id = created.Json!["id"]!.GetValue<Guid>();
        return await host.PostAsync($"{Adjustments}/{id}/post", null, key + "-post", ifMatch: created.ETag, cancellationToken: Ct);
    }

    /// <summary>Runs one step against the host and keeps the run state (item ids, draft ids and versions) in step with the replies; a relay pass returns null.</summary>
    private static async Task<CommandReply?> ExecuteStepAsync(PipelineTestHost host, RunState state, Step step, string key)
    {
        string? subject = step.AsPreparer ? TestUsers.PreparerSubject : null;
        switch (step.Kind)
        {
            case Kind.CreateItem:
                {
                    string number = $"PROP-{step.Item:000}";
                    CommandReply reply = await host.PostAsync(Items, new { companyId = Company, itemNumber = number, description = $"Property item {number}", itemType = "Stocked", baseUnit = "H87", lotTracking = "None", itemPostingGroup = "TRADE_GOODS", taxClass = "Standard", costingMethod = "MovingAverage" }, key, subject: subject, cancellationToken: Ct);
                    if (reply.Status == 200)
                    {
                        state.Items[step.Item] = reply.Json!["id"]!.GetValue<Guid>();
                    }

                    return reply;
                }

            case Kind.Create:
                {
                    CommandReply reply = await host.PostAsync(Adjustments, DraftBody(state, step.Draft!), key, cancellationToken: Ct);
                    if (reply.Status == 200)
                    {
                        state.Slots[step.Slot] = (reply.Json!["id"]!.GetValue<Guid>(), 1);
                    }

                    return reply;
                }

            case Kind.Update:
                {
                    (Guid id, string ifMatch) = Target(state, step);
                    DraftSpec draft = step.Draft!;
                    CommandReply reply = await host.PostAsync($"{Adjustments}/{id}/update", new { documentDate = "2026-06-15", reasonCode = draft.ReasonCode, warehouseId = WarehouseOf(draft.Warehouse), postingDate = draft.PostingDate, lines = Lines(state, draft) }, key, ifMatch: ifMatch, cancellationToken: Ct);
                    Advance(state, step, reply);
                    return reply;
                }

            case Kind.Discard:
                {
                    (Guid id, string ifMatch) = Target(state, step);
                    CommandReply reply = await host.PostAsync($"{Adjustments}/{id}/discard", null, key, ifMatch: ifMatch, cancellationToken: Ct);
                    Advance(state, step, reply);
                    return reply;
                }

            case Kind.Post:
                {
                    (Guid id, string ifMatch) = Target(state, step);
                    CommandReply reply = await host.PostAsync($"{Adjustments}/{id}/post", null, key, ifMatch: ifMatch, subject: subject, cancellationToken: Ct);
                    Advance(state, step, reply);
                    return reply;
                }

            default:
                await host.Relay.PublishPendingAsync(Ct);
                return null;
        }
    }

    /// <summary>The draft a step targets with the If-Match it sends; a stale step sends the previous version (R-API-02 rejects it with 412).</summary>
    private static (Guid Id, string IfMatch) Target(RunState state, Step step)
    {
        if (state.SlotOf(step.Slot) is not { } slot)
        {
            return (Guid.NewGuid(), "\"1\"");
        }

        long version = step.StaleVersion ? Math.Max(1, slot.Version - 1) : slot.Version;
        return (slot.Id, $"\"{version.ToString(CultureInfo.InvariantCulture)}\"");
    }

    private static void Advance(RunState state, Step step, CommandReply reply)
    {
        if (reply.Status == 200 && reply.ETag is { } etag && state.SlotOf(step.Slot) is { } slot)
        {
            state.Slots[slot.Slot] = (slot.Id, long.Parse(etag.Trim('"'), CultureInfo.InvariantCulture));
        }
    }

    private static object DraftBody(RunState state, DraftSpec draft) =>
        new { companyId = Company, documentDate = "2026-06-15", reasonCode = draft.ReasonCode, warehouseId = WarehouseOf(draft.Warehouse), postingDate = draft.PostingDate, lines = Lines(state, draft) };

    private static object[] Lines(RunState state, DraftSpec draft) =>
        [.. draft.Lines.Select((line, index) => new
        {
            lineNumber = index + 1,
            itemId = state.ItemOf(line),
            quantity = line.Quantity.ToString(CultureInfo.InvariantCulture),
            warehouseId = WarehouseOf(line.Warehouse),
            stockStatus = line.StockStatus,
            enteredUnitCost = line.EnteredUnitCost?.ToString(CultureInfo.InvariantCulture),
        })];

    private static Guid? WarehouseOf(int? index) => index switch { 0 => MainWarehouse, 1 => SecondWarehouse, _ => null };

    private static string Describe(IReadOnlyList<DraftSpec> drafts) => string.Join(Environment.NewLine, drafts.Select((draft, index) => $"  {index + 1,3}. CREATE and POST {draft}"));

    private static int Setting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : fallback;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = await PostgresFixture.OpenAsync(connectionString, Ct);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}

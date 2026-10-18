using System.Reflection;
using System.Text.Json;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Contracts.Documents;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.Contracts.Posting;
using Themis.Kernel.Contracts.Queries;
using Themis.Kernel.Documents;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Documents;

/// <summary>
/// The parts of step 7 that need no database: the document type registry as a catalogue (D-40) with its registry
/// rules, the shape validation of the draft commands, the number format with padding as a minimum width, the audit
/// changes (R-AUD-01), the event names (D-44), the JSON shape of the views (D-37) and the number series fixture.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DocumentFrameworkContractTests
{
    private static readonly Guid Company = Guid.Parse("00000000-0000-7000-8000-000000000104");
    private static readonly Guid Item = Guid.NewGuid();
    private static readonly Guid Warehouse = Guid.NewGuid();

    private static InventoryAdjustmentLineInput Line(int number = 1, decimal quantity = 10m, decimal? cost = null, Guid? item = null) =>
        new(number, item ?? Item, quantity, Warehouse, StockStatus.Available, cost);

    private static CreateInventoryAdjustment Create(Guid? company = null, string? externalReference = null, params InventoryAdjustmentLineInput[] lines) =>
        new(company ?? Company, BusinessDate.Parse("2026-06-15"), ReasonCode.CountDifference, Warehouse, externalReference, null, lines);

    [Fact]
    public void D_40_the_registry_lists_INVENTORY_ADJUSTMENT_with_four_transitions_and_JOURNAL_ENTRY_as_numbering_only()
    {
        DocumentTypeDefinition adjustment = DocumentTypes.Get(DocumentTypes.InventoryAdjustment);
        DocumentTypeDefinition journal = DocumentTypes.Get(DocumentTypes.JournalEntry);

        Assert.Equal(2, DocumentTypes.All.Count);
        Assert.Equal(NumberAssignment.OnPost, adjustment.NumberAssigned);
        Assert.False(adjustment.Gapless);
        Assert.True(adjustment.PostsInventory);
        Assert.True(adjustment.PostsGeneralLedger);
        Assert.Null(adjustment.PartyRole);
        Assert.Equal(
            [("CREATE", DocumentStatus.Draft, DocumentStatus.Draft, "INVENTORY_ADJUSTMENT_CREATED"), ("UPDATE", DocumentStatus.Draft, DocumentStatus.Draft, "INVENTORY_ADJUSTMENT_UPDATED"), ("DISCARD", DocumentStatus.Draft, DocumentStatus.Discarded, "INVENTORY_ADJUSTMENT_DISCARDED"), ("POST", DocumentStatus.Draft, DocumentStatus.Posted, "INVENTORY_ADJUSTED")],
            adjustment.Transitions.Select(t => (t.Command, t.From, t.To, t.EventName)));
        // R-DOC-01: only listed transitions exist; a second DISCARD or an UPDATE on a Discarded draft finds none.
        Assert.NotNull(adjustment.FindTransition("DISCARD", DocumentStatus.Draft));
        Assert.Null(adjustment.FindTransition("DISCARD", DocumentStatus.Discarded));
        Assert.Null(adjustment.FindTransition("UPDATE", DocumentStatus.Posted));

        Assert.True(journal.IsNumberingOnly);
        Assert.True(journal.Gapless);
        Assert.False(journal.PostsInventory);
        Assert.False(journal.PostsGeneralLedger);
        Assert.Empty(journal.Transitions);
        Assert.Empty(DocumentTypes.Violations(DocumentTypes.All));
    }

    [Fact]
    public void A_numbering_only_row_with_a_posting_flag_or_a_transition_and_a_type_without_CREATE_are_refused_by_the_registry_rules()
    {
        DocumentTypeDefinition postingJournal = new("JOURNAL_ENTRY", NumberAssignment.NumberingOnly, true, false, PostsGeneralLedger: true, null, []);
        DocumentTypeDefinition transitioningJournal = new("JOURNAL_ENTRY", NumberAssignment.NumberingOnly, true, false, false, null, [new("POST", DocumentStatus.Draft, DocumentStatus.Posted, "JOURNAL_POSTED")]);
        DocumentTypeDefinition noCreate = new("MANUAL_JOURNAL", NumberAssignment.OnPost, true, false, true, null, [new("POST", DocumentStatus.Draft, DocumentStatus.Posted, "JOURNAL_POSTED")]);
        DocumentTypeDefinition duplicate = new("MANUAL_JOURNAL", NumberAssignment.OnPost, true, false, true, null, [new("CREATE", DocumentStatus.Draft, DocumentStatus.Draft, "A"), new("CREATE", DocumentStatus.Draft, DocumentStatus.Draft, "B")]);

        Assert.Single(DocumentTypes.Violations([postingJournal]));
        Assert.Single(DocumentTypes.Violations([transitioningJournal]));
        Assert.Single(DocumentTypes.Violations([noCreate]));
        Assert.Single(DocumentTypes.Violations([duplicate]));
        Assert.Contains(DocumentTypes.Violations([noCreate, noCreate]), violation => violation.Contains("twice", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new DocumentTransition("post", DocumentStatus.Draft, DocumentStatus.Posted, "X"));
    }

    [Fact]
    public void The_commands_name_their_permissions_and_the_create_scope_is_the_company_in_the_body()
    {
        Assert.Equal("INVENTORY_ADJUSTMENT.CREATE", typeof(CreateInventoryAdjustment).GetCustomAttribute<CommandAttribute>()!.PermissionCode);
        Assert.Equal("INVENTORY_ADJUSTMENT.UPDATE", typeof(UpdateInventoryAdjustment).GetCustomAttribute<CommandAttribute>()!.PermissionCode);
        Assert.Equal("INVENTORY_ADJUSTMENT.DISCARD", typeof(DiscardInventoryAdjustment).GetCustomAttribute<CommandAttribute>()!.PermissionCode);
        Assert.Equal("INVENTORY_ADJUSTMENT.POST", typeof(PostInventoryAdjustment).GetCustomAttribute<CommandAttribute>()!.PermissionCode);
        Assert.Equal("INVENTORY_ADJUSTMENT.READ", typeof(GetInventoryAdjustment).GetCustomAttribute<QueryAttribute>()!.PermissionCode);
        Assert.Equal(Company, Create(lines: Line()).Scope.CompanyId);
        Assert.True(typeof(IVersionedCommand).IsAssignableFrom(typeof(UpdateInventoryAdjustment)));
        Assert.True(typeof(IVersionedCommand).IsAssignableFrom(typeof(DiscardInventoryAdjustment)));
        Assert.True(typeof(IVersionedCommand).IsAssignableFrom(typeof(PostInventoryAdjustment)));
        Assert.Equal(4, DocumentRoutes.Commands.Count);
        Assert.Empty(new CreateInventoryAdjustmentValidator().Validate(Create(lines: [Line(1), Line(2, -3m, 2.5m)])));
        Assert.Empty(new CreateInventoryAdjustmentValidator().Validate(Create()));
    }

    [Theory]
    [InlineData("company", "company_required")]
    [InlineData("reference", "external_reference_too_long")]
    [InlineData("zero-number", "line_number_not_positive")]
    [InlineData("duplicate", "line_number_duplicate")]
    [InlineData("item", "line_item_required")]
    [InlineData("quantity", "line_quantity_zero")]
    [InlineData("cost-negative", "line_entered_unit_cost_negative")]
    [InlineData("cost-precision", "line_entered_unit_cost_precision")]
    public void A_shape_error_is_a_validation_error_with_its_own_code_and_no_ruleId_before_the_transaction(string which, string code)
    {
        CreateInventoryAdjustment command = which switch
        {
            "company" => Create(company: Guid.Empty, lines: Line()),
            "reference" => Create(externalReference: new string('x', 201), lines: Line()),
            "zero-number" => Create(lines: Line(0)),
            "duplicate" => Create(lines: [Line(1), Line(1)]),
            "item" => Create(lines: Line(item: Guid.Empty)),
            "quantity" => Create(lines: Line(quantity: 0m)),
            "cost-negative" => Create(lines: Line(cost: -1m)),
            _ => Create(lines: Line(cost: 0.00000000001m)),
        };

        Error error = Assert.Single(new CreateInventoryAdjustmentValidator().Validate(command));
        Error? onUpdate = new UpdateInventoryAdjustmentValidator().Validate(new UpdateInventoryAdjustment(Guid.NewGuid(), 1, command.DocumentDate, command.ReasonCode, command.WarehouseId, command.ExternalReference, null, command.Lines)).SingleOrDefault();

        Assert.Equal(code, error.Code);
        Assert.Null(error.RuleId);
        Assert.Equal(ErrorKind.Validation, error.Kind);
        // The same line and header checks run for UPDATE; only the company is the create command's own.
        Assert.Equal(which == "company" ? null : code, onUpdate?.Code);
    }

    [Fact]
    public void Padding_is_a_minimum_width_so_the_number_after_IA_999999_is_IA_1000000()
    {
        Assert.Equal("IA-000001", NumberFormat.Format("IA-", 6, 1));
        Assert.Equal("IA-999999", NumberFormat.Format("IA-", 6, 999999));
        Assert.Equal("IA-1000000", NumberFormat.Format("IA-", 6, 1000000));
        Assert.Equal("JE2026-000001", NumberFormat.Format("JE2026-", 6, 1));
        Assert.Equal("7", NumberFormat.Format(null, 0, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberFormat.Format("X", 6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumberFormat.Format("X", 21, 1));
    }

    [Fact]
    public void The_milestone_1_fixture_holds_the_IA_continuous_series_and_the_gapless_JE2026_series_per_fiscal_year()
    {
        Guid fiscalYear = Guid.NewGuid();
        NumberSeriesFixtureDefinition fixture = Milestone1NumberSeriesFixture.For(Company, fiscalYear);

        Assert.Equal(2, fixture.Series.Count);
        NumberSeriesDefinition adjustment = fixture.Series.Single(s => s.DocumentType == DocumentTypes.InventoryAdjustment);
        NumberSeriesDefinition journal = fixture.Series.Single(s => s.DocumentType == DocumentTypes.JournalEntry);
        Assert.Equal((NumberSeriesScope.Continuous, (Guid?)null, "IA-", 6, 1L, false), (adjustment.Scope, adjustment.FiscalYearId, adjustment.Prefix, adjustment.Padding, adjustment.NextNumber, adjustment.Gapless));
        Assert.Equal((NumberSeriesScope.PerFiscalYear, (Guid?)fiscalYear, "JE2026-", 6, 1L, true), (journal.Scope, journal.FiscalYearId, journal.Prefix, journal.Padding, journal.NextNumber, journal.Gapless));
        Assert.All(fixture.Series, series => Assert.Equal(Company, series.CompanyId));
        Assert.Equal("IA-000001", NumberFormat.Format(adjustment.Prefix, adjustment.Padding, adjustment.NextNumber));
        Assert.Equal("JE2026-000001", NumberFormat.Format(journal.Prefix, journal.Padding, journal.NextNumber));
    }

    [Fact]
    public void R_AUD_01_the_audit_changes_hold_the_fields_that_differ_and_an_unchanged_line_is_left_alone()
    {
        Guid document = Guid.NewGuid();
        DocumentLineRow line = new(Guid.NewGuid(), document, 1, LineStatus.Open, Item, "Hex bolt M8", 10m, "H87", 10m, Warehouse, StockStatus.Available, 2.5m, null, null, 1, 0);
        DocumentRow header = new(document, Company, "INVENTORY_ADJUSTMENT", DocumentStatus.Draft, null, document.ToString(), new DateOnly(2026, 6, 15), null, "SEK", Warehouse, null, null, null, 1, ReasonCode.CountDifference, 1);

        IReadOnlyDictionary<string, FieldChange> created = DocumentAudit.LineChanges(null, line);
        IReadOnlyDictionary<string, FieldChange> cancelled = DocumentAudit.LineChanges(line, line with { LineStatus = LineStatus.Cancelled, Version = 2 });
        IReadOnlyDictionary<string, FieldChange> discarded = DocumentAudit.DocumentChanges(header, header with { Status = DocumentStatus.Discarded, Version = 2 });
        IReadOnlyDictionary<string, FieldChange> extension = DocumentAudit.InventoryAdjustmentChanges(null, header);

        Assert.Equal(12, created.Count);
        Assert.All(created.Values, change => Assert.Null(change.Before));
        Assert.Equal("10", created["quantity"].After);
        Assert.Equal("2.5", created["enteredUnitCost"].After);
        Assert.Equal(["lineStatus", "version"], cancelled.Keys.Order());
        Assert.Equal(("Open", "Cancelled"), (cancelled["lineStatus"].Before, cancelled["lineStatus"].After));
        Assert.Equal(["status", "version"], discarded.Keys.Order());
        Assert.Equal("2026-06-15", DocumentAudit.DocumentChanges(null, header)["documentDate"].After);
        Assert.Equal("CountDifference", extension["reasonCode"].After);
        Assert.True(DocumentAudit.SameContent(line, line with { Id = Guid.NewGuid(), Version = 7, Quantity = 10.000m }));
        Assert.False(DocumentAudit.SameContent(line, line with { Quantity = 11m }));
        Assert.False(DocumentAudit.SameContent(line, line with { EnteredUnitCost = null }));
    }

    [Fact]
    public void D_44_the_registry_names_the_events_and_D_37_the_view_serialises_decimals_as_strings_and_the_command_reads_lines()
    {
        Assert.Equal("INVENTORY_ADJUSTMENT_UPDATED", EventNames.Generated(DocumentTypes.InventoryAdjustment, DocumentTypes.UpdateVerb));
        Assert.Equal("INVENTORY_ADJUSTED", DocumentTypes.Get(DocumentTypes.InventoryAdjustment).FindTransition("POST", DocumentStatus.Draft)!.EventName);

        InventoryAdjustmentLineView lineView = new(Guid.NewGuid(), 1, LineStatus.Open, Item, "Hex bolt M8", -2.500m, UnitCode.From("KGM"), -2.500m, Warehouse, StockStatus.Blocked, 1.25m, null, null, 1);
        InventoryAdjustmentView view = new(Guid.NewGuid(), Company, "INVENTORY_ADJUSTMENT", DocumentStatus.Draft, null, "ref", BusinessDate.Parse("2026-06-15"), null, CurrencyCode.From("SEK"), null, null, ReasonCode.Scrap, null, null, 1, [lineView]);

        string json = JsonSerializer.Serialize(view, ThemisJson.Options);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement line = document.RootElement.GetProperty("lines")[0];
        CreateInventoryAdjustment command = JsonSerializer.Deserialize<CreateInventoryAdjustment>($$"""{"companyId": "{{Company}}", "documentDate": "2026-06-15", "reasonCode": "CountDifference", "lines": [{"lineNumber": 1, "itemId": "{{Item}}", "quantity": "10"}, {"lineNumber": 2, "itemId": "{{Item}}", "quantity": -3, "stockStatus": "Blocked", "enteredUnitCost": "2.50"}]}""", ThemisJson.Options)!;

        Assert.Equal("Draft", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("documentNumber").ValueKind);
        Assert.Equal("2026-06-15", document.RootElement.GetProperty("documentDate").GetString());
        Assert.Equal("SEK", document.RootElement.GetProperty("currency").GetString());
        Assert.Equal("Scrap", document.RootElement.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("version").ValueKind);
        Assert.Equal("-2.500", line.GetProperty("quantity").GetString());
        Assert.Equal("1.25", line.GetProperty("enteredUnitCost").GetString());
        Assert.Equal("KGM", line.GetProperty("unitCode").GetString());
        Assert.Equal("Blocked", line.GetProperty("stockStatus").GetString());
        Assert.Equal(2, command.Lines!.Count);
        Assert.Equal(10m, command.Lines[0].Quantity);
        Assert.Equal(StockStatus.Available, command.Lines[0].StockStatus);
        Assert.Null(command.Lines[0].WarehouseId);
        Assert.Equal(-3m, command.Lines[1].Quantity);
        Assert.Equal(2.50m, command.Lines[1].EnteredUnitCost);
        Assert.Equal(ReasonCode.CountDifference, command.ReasonCode);
    }
}

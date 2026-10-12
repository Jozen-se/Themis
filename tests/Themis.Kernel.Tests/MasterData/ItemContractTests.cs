using System.Text.Json;
using Themis.Kernel.Contracts.MasterData;
using Themis.Kernel.Contracts.Messaging;
using Themis.Kernel.Contracts.Organisation;
using Themis.Kernel.Contracts.Persistence;
using Themis.Kernel.MasterData;
using Themis.Kernel.Persistence.Migrations;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.MasterData;

/// <summary>
/// The parts of step 5 that need no database: the shape validation of ITEM.CREATE, the audit changes of the two
/// rows (R-AUD-01), the event names (D-44), the JSON shape of the views (D-37), the reference rows of the migration
/// and the fixture definition.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ItemContractTests
{
    private static readonly Guid Company = Guid.Parse("00000000-0000-7000-8000-000000000104");

    private static CreateItem Command(string description = "Hex bolt M8", decimal? weight = null, decimal? multiple = null, decimal? minimum = null, Guid? company = null) =>
        new(company ?? Company, Code.From("TEST-001"), description, ItemType.Stocked, UnitCode.From("H87"), LotTracking.None, Code.From("TRADE_GOODS"), TaxClass.Standard, CostingMethod.MovingAverage, NetWeightKg: weight, SalesMultiple: multiple, MinOrderQty: minimum);

    [Fact]
    public void A_well_formed_command_passes_and_its_scope_is_the_company_in_the_body()
    {
        CreateItem command = Command(weight: 0.012m, multiple: 100m, minimum: 100m);

        Assert.Empty(new CreateItemValidator().Validate(command));
        Assert.Equal(Company, command.Scope.CompanyId);
        Assert.Null(command.Scope.SiteId);
        Assert.Equal("ITEM.CREATE", typeof(CreateItem).GetCustomAttributes(typeof(Themis.Kernel.Contracts.Commands.CommandAttribute), false).Cast<Themis.Kernel.Contracts.Commands.CommandAttribute>().Single().PermissionCode);
        Assert.Equal("ITEM.READ", typeof(GetItem).GetCustomAttributes(typeof(Themis.Kernel.Contracts.Queries.QueryAttribute), false).Cast<Themis.Kernel.Contracts.Queries.QueryAttribute>().Single().PermissionCode);
    }

    [Theory]
    [InlineData("company", "company_required")]
    [InlineData("blank", "description_required")]
    [InlineData("long", "description_too_long")]
    [InlineData("weight", "net_weight_kg_negative")]
    [InlineData("weight-precision", "net_weight_kg_precision")]
    [InlineData("multiple", "sales_multiple_not_positive")]
    [InlineData("minimum", "min_order_qty_not_positive")]
    public void A_shape_error_is_a_validation_error_with_its_own_code_and_no_ruleId(string which, string code)
    {
        CreateItem command = which switch
        {
            "company" => Command(company: Guid.Empty),
            "blank" => Command(description: "   "),
            "long" => Command(description: new string('x', CreateItemValidator.DescriptionMaxLength + 1)),
            "weight" => Command(weight: -0.001m),
            "weight-precision" => Command(weight: 0.0000001m),
            "multiple" => Command(multiple: 0m),
            _ => Command(minimum: -1m),
        };

        Error error = Assert.Single(new CreateItemValidator().Validate(command));

        Assert.Equal(code, error.Code);
        Assert.Null(error.RuleId);
        Assert.Equal(ErrorKind.Validation, error.Kind);
    }

    [Fact]
    public void D_44_the_event_names_carry_the_type_of_the_created_row()
    {
        Assert.Equal("ITEM_CREATED", EventNames.Generated(CreateItemHandler.ItemType, "CREATE"));
        Assert.Equal("ITEM_COMPANY_CREATED", EventNames.Generated(CreateItemHandler.ItemCompanyType, "CREATE"));
    }

    [Fact]
    public void R_AUD_01_the_audit_changes_hold_every_field_from_nothing_to_its_created_value()
    {
        ItemCompanyView row = new(Guid.NewGuid(), Company, MasterDataStatus.Active, Code.From("TRADE_GOODS"), TaxClass.Standard, CostingMethod.MovingAverage, true, false, null, 1);
        ItemView item = new(Guid.NewGuid(), Code.From("TEST-001"), "Hex bolt M8", ItemType.Stocked, UnitCode.From("H87"), LotTracking.None, 0.012m, null, 100m, 1, row);

        IReadOnlyDictionary<string, FieldChange> itemChanges = ItemAudit.ItemChanges(item);
        IReadOnlyDictionary<string, FieldChange> rowChanges = ItemAudit.ItemCompanyChanges(item.Id, row);

        Assert.Equal(8, itemChanges.Count);
        Assert.Equal(9, rowChanges.Count);
        Assert.All(itemChanges.Values.Concat(rowChanges.Values), change => Assert.Null(change.Before));
        Assert.Equal("TEST-001", itemChanges["itemNumber"].After);
        Assert.Equal("0.012", itemChanges["netWeightKg"].After);
        Assert.Null(itemChanges["salesMultiple"].After);
        Assert.Equal("100", itemChanges["minOrderQty"].After);
        Assert.Equal(item.Id.ToString(), rowChanges["itemId"].After);
        Assert.Equal("False", rowChanges["purchaseAllowed"].After);
        Assert.Null(rowChanges["defaultWarehouseId"].After);
    }

    [Fact]
    public void D_37_the_item_view_serialises_decimals_as_strings_enums_as_names_and_the_command_reads_both_forms()
    {
        ItemCompanyView row = new(Guid.NewGuid(), Company, MasterDataStatus.Active, Code.From("TRADE_GOODS"), TaxClass.Reduced, CostingMethod.MovingAverage, true, true, null, 1);
        ItemView item = new(Guid.NewGuid(), Code.From("TEST-001"), "Hex bolt M8", ItemType.NonStocked, UnitCode.From("KGM"), LotTracking.Required, 0.0120m, null, null, 1, row);

        string json = JsonSerializer.Serialize(item, ThemisJson.Options);
        using JsonDocument document = JsonDocument.Parse(json);
        CreateItem command = JsonSerializer.Deserialize<CreateItem>($$"""{"companyId": "{{Company}}", "itemNumber": "test-001", "description": "x", "itemType": "Stocked", "baseUnit": "h87", "lotTracking": "None", "itemPostingGroup": "trade_goods", "taxClass": "Standard", "costingMethod": "MovingAverage", "netWeightKg": 0.5, "salesMultiple": "10"}""", ThemisJson.Options)!;

        Assert.Equal("0.0120", document.RootElement.GetProperty("netWeightKg").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("salesMultiple").ValueKind);
        Assert.Equal("NonStocked", document.RootElement.GetProperty("itemType").GetString());
        Assert.Equal("Required", document.RootElement.GetProperty("lotTracking").GetString());
        Assert.Equal("Reduced", document.RootElement.GetProperty("company").GetProperty("taxClass").GetString());
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("version").ValueKind);
        Assert.Equal("TEST-001", command.ItemNumber.Value);
        Assert.Equal("H87", command.BaseUnit.Value);
        Assert.Equal("TRADE_GOODS", command.ItemPostingGroup.Value);
        Assert.Equal(0.5m, command.NetWeightKg);
        Assert.Equal(10m, command.SalesMultiple);
        Assert.True(command.SalesAllowed);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateItem>($$"""{"companyId": "{{Company}}", "itemNumber": "x", "description": "x", "itemType": "Stocked", "baseUnit": "H87", "lotTracking": "None", "itemPostingGroup": "G", "taxClass": "Standard", "costingMethod": "Fifo"}""", ThemisJson.Options));
    }

    [Fact]
    public void The_migration_seeds_the_starter_units_and_currencies_with_fixed_name_derived_ids()
    {
        Assert.Equal(["H87", "KGM", "MTR"], MasterDataTables.Units.Select(unit => unit.Code));
        Assert.Equal([0, 3, 3], MasterDataTables.Units.Select(unit => unit.Decimals));
        Assert.Equal(["Count", "Mass", "Length"], MasterDataTables.Units.Select(unit => unit.Dimension));
        Assert.Equal(["SEK", "EUR"], MasterDataTables.Currencies.Select(currency => currency.Code));
        Assert.All(MasterDataTables.Currencies, currency => Assert.Equal(2, currency.MinorUnits));
        Assert.All(MasterDataTables.Units, unit => Assert.True(Enum.TryParse<UnitDimension>(unit.Dimension, out _)));

        string h87 = MasterDataTables.ReferenceRowId("unit_of_measure", "H87");
        Assert.Equal(h87, MasterDataTables.ReferenceRowId("unit_of_measure", "H87"));
        Assert.NotEqual(h87, MasterDataTables.ReferenceRowId("currency", "H87"));
        Assert.Equal('7', h87[14]);
        Assert.Contains(h87[19], "89ab");
    }

    [Fact]
    public void The_milestone_1_fixture_holds_the_posting_group_TRADE_GOODS_only()
    {
        ItemPostingGroupDefinition group = Assert.Single(Milestone1MasterDataFixture.Definition.ItemPostingGroups);

        Assert.Equal(Milestone1MasterDataFixture.TradeGoodsId, group.Id);
        Assert.Equal("TRADE_GOODS", group.Code.Value);
        Assert.Equal("Trade goods", group.Name);
    }
}

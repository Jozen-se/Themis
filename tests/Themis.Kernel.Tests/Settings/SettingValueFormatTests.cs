using Themis.Kernel.Contracts.Settings;
using Themis.Kernel.Primitives;
using Themis.Kernel.Settings;

namespace Themis.Kernel.Tests.Settings;

/// <summary>The text form of setting values and their validation (Phase 0 Specification, SettingDefinition; D-58): a violation is a 400, never R-SET-01.</summary>
[Trait("Category", "Unit")]
public sealed class SettingValueFormatTests
{
    private static readonly IReadOnlySet<SettingLevel> Installation = new HashSet<SettingLevel> { SettingLevel.Installation };

    private static SettingDefinition Definition(SettingValueType type, string? min = null, string? max = null, IReadOnlyList<string>? allowed = null) =>
        new("test.value", type, null, Installation, SettingBacking.Setting, "R-API-01", "A test definition.", Min: min, Max: max, AllowedValues: allowed);

    [Theory]
    [InlineData(SettingValueType.Integer, " 42 ", "42")]
    [InlineData(SettingValueType.Integer, "-7", "-7")]
    [InlineData(SettingValueType.Decimal, "12.50", "12.50")]
    [InlineData(SettingValueType.Boolean, "true", "true")]
    [InlineData(SettingValueType.Code, "HalfEven", "HalfEven")]
    [InlineData(SettingValueType.Duration, "00:05:00", "00:05:00")]
    public void A_valid_value_is_normalised_to_its_canonical_text(SettingValueType type, string given, string canonical)
    {
        Result<string> result = SettingValueFormat.Normalise(Definition(type), given);
        Assert.True(result.IsSuccess, result.Primary?.Message);
        Assert.Equal(canonical, result.Value);
    }

    [Theory]
    [InlineData(SettingValueType.Integer, "4.5")]
    [InlineData(SettingValueType.Integer, "")]
    [InlineData(SettingValueType.Decimal, "1e3")]
    [InlineData(SettingValueType.Boolean, "True")]
    [InlineData(SettingValueType.Boolean, "yes")]
    [InlineData(SettingValueType.Code, "has space")]
    [InlineData(SettingValueType.Code, "semi;colon")]
    [InlineData(SettingValueType.Duration, "5 minutes")]
    [InlineData(SettingValueType.Duration, "-00:01:00")]
    public void D_63_a_value_of_the_wrong_form_a_Code_value_failing_the_token_check_included_is_a_400_with_the_field_in_the_details(SettingValueType type, string given)
    {
        Result<string> result = SettingValueFormat.Normalise(Definition(type), given);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Primary!.Kind);
        Assert.Equal(SettingValueFormat.ValueInvalidCode, result.Primary.Code);
        Assert.Null(result.Primary.RuleId);
        Assert.Equal("value", result.Primary.Details!["field"]);
    }

    [Fact]
    public void Min_max_and_allowed_values_bound_the_value_and_the_bounds_are_inclusive()
    {
        SettingDefinition days = Definition(SettingValueType.Integer, min: "1", max: "90");
        Assert.True(SettingValueFormat.IsValid(days, "1"));
        Assert.True(SettingValueFormat.IsValid(days, "90"));
        Assert.False(SettingValueFormat.IsValid(days, "0"));
        Assert.False(SettingValueFormat.IsValid(days, "91"));

        SettingDefinition percent = Definition(SettingValueType.Decimal, min: "0", max: "100");
        Assert.True(SettingValueFormat.IsValid(percent, "100.0"));
        Assert.False(SettingValueFormat.IsValid(percent, "100.01"));

        SettingDefinition mode = Definition(SettingValueType.Code, allowed: ["HalfAwayFromZero", "HalfEven"]);
        Assert.True(SettingValueFormat.IsValid(mode, "HalfEven"));
        Assert.False(SettingValueFormat.IsValid(mode, "Floor"));
        Assert.Contains("HalfAwayFromZero, HalfEven", SettingValueFormat.Normalise(mode, "Floor").Primary!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_milestone_2_default_passes_its_own_definition()
    {
        foreach (SettingDefinition definition in SettingCatalog.Definitions.Where(definition => definition.DefaultValue is not null))
        {
            Assert.True(SettingValueFormat.IsValid(definition, definition.DefaultValue), definition.Key);
        }
    }
}

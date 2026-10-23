using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Primitives;
using Themis.Kernel.Settings;

namespace Themis.Kernel.Tests.Primitives;

/// <summary>
/// RR-82: the validation patterns anchor at the end of input (\z), so an otherwise valid value followed by a newline
/// is refused by the exact check. The factories that trim their input first (Code.From and the other codes) are not
/// what these tests prove; the checks on the exact text are.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TrailingNewlineTests
{
    [Theory]
    [InlineData("corr-1")]
    [InlineData("a.b:c_d-e")]
    public void RR_82_a_correlation_id_with_a_trailing_newline_is_refused(string valid)
    {
        Assert.True(CorrelationId.TryFrom(valid, out _));
        Assert.False(CorrelationId.TryFrom(valid + "\n", out _));
    }

    [Fact]
    public void RR_82_an_idempotency_key_with_a_trailing_newline_is_refused()
    {
        Assert.True(IdempotencyKey.TryFrom("key-1", out _));
        Assert.False(IdempotencyKey.TryFrom("key-1\n", out _));
    }

    [Fact]
    public void RR_82_a_code_with_a_trailing_newline_is_refused()
    {
        Assert.True(Code.TryFrom("TRADE_GOODS", out _));
        Assert.False(Code.TryFrom("TRADE_GOODS\n", out _));
    }

    [Fact]
    public void RR_82_a_currency_code_with_a_trailing_newline_is_refused()
    {
        Assert.True(CurrencyCode.TryFrom("SEK", out _));
        Assert.False(CurrencyCode.TryFrom("SEK\n", out _));
    }

    [Fact]
    public void RR_82_a_country_code_with_a_trailing_newline_is_refused()
    {
        Assert.True(CountryCode.TryFrom("SE", out _));
        Assert.False(CountryCode.TryFrom("SE\n", out _));
    }

    [Fact]
    public void RR_82_a_unit_code_with_a_trailing_newline_is_refused()
    {
        Assert.True(UnitCode.TryFrom("PCS", out _));
        Assert.False(UnitCode.TryFrom("PCS\n", out _));
    }

    [Fact]
    public void RR_82_the_setting_token_check_refuses_a_value_with_a_trailing_newline()
    {
        Assert.True(SettingValueFormat.IsCodeToken("HalfEven"));
        Assert.False(SettingValueFormat.IsCodeToken("HalfEven\n"));
    }
}

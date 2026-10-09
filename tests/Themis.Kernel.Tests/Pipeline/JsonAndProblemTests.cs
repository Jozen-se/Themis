using System.Text.Json;
using Themis.Kernel.Contracts.Commands;
using Themis.Kernel.Pipeline.Http;
using Themis.Kernel.Pipeline.Json;
using Themis.Kernel.Primitives;

namespace Themis.Kernel.Tests.Pipeline;

/// <summary>JSON numbers (D-37), problem details (R-API-03) and the If-Match parsing of the HTTP layer (R-API-02).</summary>
[Trait("Category", "Unit")]
public sealed class JsonAndProblemTests
{
    private sealed record Sample(decimal Amount, decimal? Optional, long Version, int Count);

    [Fact]
    public void D_37_decimals_serialise_as_strings_with_their_scale_and_integers_as_numbers()
    {
        string json = JsonSerializer.Serialize(new Sample(2_500_000.00m, null, 7, 3), ThemisJson.Options);

        Assert.Equal("""{"amount":"2500000.00","optional":null,"version":7,"count":3}""", json);
    }

    [Theory]
    [InlineData("""{"amount":"12.50","optional":"0.1","version":1,"count":1}""", "12.50")]
    [InlineData("""{"amount":12.5,"optional":0.1,"version":1,"count":1}""", "12.5")]
    [InlineData("""{"amount":"-0.0001","optional":null,"version":1,"count":1}""", "-0.0001")]
    public void D_37_decimals_deserialise_from_strings_and_numbers(string json, string expected)
    {
        Sample sample = JsonSerializer.Deserialize<Sample>(json, ThemisJson.Options)!;

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), sample.Amount);
        Assert.Equal(expected, sample.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("""{"amount":"1e3","optional":null,"version":1,"count":1}""")]
    [InlineData("""{"amount":"twelve","optional":null,"version":1,"count":1}""")]
    [InlineData("""{"amount":true,"optional":null,"version":1,"count":1}""")]
    public void D_37_exponents_words_and_booleans_are_not_decimals(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Sample>(json, ThemisJson.Options));
    }

    [Fact]
    public void R_API_03_problem_details_carry_status_code_rule_id_and_every_error()
    {
        CommandResponse response = ProblemDetailsBody.ToResponse(
            [Error.Rule("R-LED-04", "period_closed", "closed"), Error.Validation("quantity_zero", "zero")],
            CorrelationId.From("corr-1"));

        Assert.Equal(422, response.StatusCode);
        Assert.Equal(CommandResponse.ProblemContentType, response.ContentType);
        JsonElement body = JsonDocument.Parse(response.Body).RootElement;
        Assert.Equal("period_closed", body.GetProperty("code").GetString());
        Assert.Equal("R-LED-04", body.GetProperty("ruleId").GetString());
        Assert.Equal(422, body.GetProperty("status").GetInt32());
        Assert.Equal("corr-1", body.GetProperty("correlationId").GetString());
        Assert.Equal(2, body.GetProperty("errors").GetArrayLength());
        Assert.Equal("quantity_zero", body.GetProperty("errors")[1].GetProperty("code").GetString());
    }

    [Fact]
    public void R_API_03_a_problem_without_a_rule_omits_the_rule_id_instead_of_writing_null()
    {
        CommandResponse response = ProblemDetailsBody.ToResponse(Error.NotFound("item_not_found", "no"), CorrelationId.From("corr-2"));

        Assert.Equal(404, response.StatusCode);
        Assert.DoesNotContain("ruleId", response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.PreconditionFailed, 412)]
    [InlineData(ErrorKind.RuleViolation, 422)]
    public void Each_error_kind_maps_to_one_status(ErrorKind kind, int status)
    {
        Assert.Equal(status, ProblemDetailsBody.StatusFor(kind));
    }

    [Theory]
    [InlineData("\"7\"", 7)]
    [InlineData("W/\"7\"", 7)]
    [InlineData("7", 7)]
    [InlineData(" \"0\" ", 0)]
    public void R_API_02_If_Match_accepts_strong_weak_and_bare_versions(string header, long expected)
    {
        Assert.True(CommandEndpoints.TryParseVersion(header, out long version));
        Assert.Equal(expected, version);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("\"-1\"")]
    [InlineData("\"seven\"")]
    [InlineData("")]
    [InlineData(null)]
    public void R_API_02_If_Match_refuses_anything_but_a_version(string? header)
    {
        Assert.False(CommandEndpoints.TryParseVersion(header, out _));
    }
}

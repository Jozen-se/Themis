namespace Themis.Integration.Tests.Hardening;

/// <summary>
/// RR-70: Themis.Migrator loads a fixture only when the environment is Development, Test or Testing, compared without
/// regard to letter case; unset means Production. The refusal is exit code 9 with a message naming the environment
/// and the fixture, given before any connection is opened, so an unreachable server still answers 9, not 3. The
/// argument checks come first: an unknown fixture is still a usage error (1).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Integration")]
public sealed class MigratorFixtureGuardTests
{
    /// <summary>Nothing listens here: a run that tried to connect would answer 3.</summary>
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=themis;Username=themis_migrator;Password=unused;Timeout=2";

    private readonly PostgresFixture _db;

    public MigratorFixtureGuardTests(PostgresFixture db)
    {
        _db = db;
    }

    [Theory]
    [InlineData(null, "Production (DOTNET_ENVIRONMENT is unset)")]
    [InlineData("", "Production (DOTNET_ENVIRONMENT is unset)")]
    [InlineData("Production", "Production")]
    [InlineData("Staging", "Staging")]
    public async Task RR_70_a_fixture_outside_Development_Test_or_Testing_exits_9_before_any_connection_and_names_the_environment_and_fixture(string? environment, string named)
    {
        (int exitCode, string error) = await RunCapturingErrorAsync(["--connection", Unreachable, "--fixture", "milestone-1"], environment);

        Assert.Equal(9, exitCode);
        Assert.Contains($"Fixture 'milestone-1' refused in environment '{named}'", error, StringComparison.Ordinal);
        Assert.Contains("RR-70", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RR_70_an_unknown_fixture_is_still_a_usage_error_in_Production()
    {
        (int exitCode, _) = await RunCapturingErrorAsync(["--connection", Unreachable, "--fixture", "milestone-9"], "Production");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RR_70_without_a_fixture_the_environment_is_not_checked()
    {
        // No fixture asked for: the run goes on to connect, so the unreachable server answers 3.
        (int exitCode, _) = await RunCapturingErrorAsync(["--connection", Unreachable], "Production");

        Assert.Equal(3, exitCode);
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Testing")]
    [InlineData("development")]
    public async Task RR_70_the_fixture_runs_under_Development_Test_and_Testing_in_any_letter_case(string environment)
    {
        Assert.Equal(0, await Themis.Migrator.Program.RunAsync(["--connection", _db.ConnectionStringFor("themis_migrator"), "--fixture", "milestone-1"], environment));
    }

    private static async Task<(int ExitCode, string Error)> RunCapturingErrorAsync(string[] args, string? environment)
    {
        TextWriter original = Console.Error;
        using StringWriter captured = new();
        Console.SetError(captured);
        try
        {
            int exitCode = await Themis.Migrator.Program.RunAsync(args, environment);
            return (exitCode, captured.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }
}

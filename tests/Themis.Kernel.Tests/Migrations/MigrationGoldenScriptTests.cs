using System.Text.RegularExpressions;
using Themis.Kernel.Persistence;

namespace Themis.Kernel.Tests.Migrations;

/// <summary>
/// RR-39 (Build Brief, Migrations): a migration's SQL is fixed once it is merged. Each migration's script, generated
/// on its own from the migration before it with no database and not idempotent, equals the golden file committed
/// under db/migrations/golden; a new migration adds its own golden file. The EF product version in the history row
/// and the line endings are normalised. A provider or EF upgrade that changes a generated script needs the owner's
/// review before a golden file is updated. Set THEMIS_GOLDEN_RECORD=1 to write the golden files that do not exist yet
/// (RR-137).
/// RR-135: the product version is the value EF writes into the history row, not SQL the migration owns, so an EF patch
/// update changes it in every script while the migration itself is unchanged; it is masked on both sides, and a guard
/// proves the mask matches every golden file, so it cannot silently stop applying again.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class MigrationGoldenScriptTests
{
    private const string RecordVariable = "THEMIS_GOLDEN_RECORD";

    // RR-39, RR-135: the history insert as EF generates it for PostgreSQL. The table name is unquoted in the scripts
    // (kernel.__ef_migrations_history); the earlier pattern required it quoted, so it never matched and the mask was
    // never applied. Only the version literal is replaced; the migration id and every other character are compared.
    [GeneratedRegex(@"(INSERT INTO kernel\.""?__ef_migrations_history""? \(""MigrationId"", ""ProductVersion""\)\s*VALUES \('[^']+', ')[^']+('\);)")]
    private static partial Regex ProductVersion();

    public static TheoryData<string> Migrations()
    {
        TheoryData<string> data = [];
        foreach (string id in KernelMigrations.All())
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Migrations))]
    public void RR_39_the_generated_script_of_a_merged_migration_equals_its_golden_file(string migrationId)
    {
        IReadOnlyList<string> all = KernelMigrations.All();
        int index = all.ToList().IndexOf(migrationId);
        string? previous = index == 0 ? null : all[index - 1];
        string script = KernelMigrations.GenerateScript(previous, migrationId);
        string generated = Normalise(script);
        string golden = Normalise(ReadGolden(GoldenDirectory(), migrationId, script, Environment.GetEnvironmentVariable(RecordVariable) == "1"));
        Assert.True(golden == generated, $"The generated script of {migrationId} differs from db/migrations/golden/{migrationId}.sql. A merged migration's SQL is fixed (RR-39); a provider or EF upgrade that changes the generated script needs the owner's review before the golden file is updated.{Environment.NewLine}{FirstDifference(golden, generated)}");
    }

    [Fact]
    public void RR_135_the_mask_matches_the_history_insert_of_every_golden_file_exactly_once()
    {
        // Without this guard a pattern that matches nothing passes silently and leaves the version compared (RR-135).
        foreach (string path in Directory.GetFiles(GoldenDirectory(), "*.sql"))
        {
            Assert.True(ProductVersion().Count(File.ReadAllText(path)) == 1, $"{Path.GetFileName(path)}: the product-version mask does not match its history insert exactly once.");
        }
    }

    [Fact]
    public void RR_135_only_the_product_version_value_is_ignored_and_any_other_change_to_a_merged_script_still_fails()
    {
        // A golden file that exists: the newest committed one, not the newest migration, whose file a record run may not have written yet.
        string path = Directory.GetFiles(GoldenDirectory(), "*.sql").Order(StringComparer.Ordinal).Last();
        string migrationId = Path.GetFileNameWithoutExtension(path);
        string golden = File.ReadAllText(path);
        string versionOnly = ProductVersion().Replace(golden, "${1}10.0.999$2");

        Assert.NotEqual(golden, versionOnly);
        Assert.Equal(Normalise(golden), Normalise(versionOnly));

        // Each mutation leaves the history row intact and still masked, so a difference can only come from the mutated text.
        string[] mutations =
        [
            golden.Replace($"'{migrationId}'", "'20990101000000_Other'", StringComparison.Ordinal),
            golden.Insert(golden.IndexOf('\n', StringComparison.Ordinal) + 1, "SELECT 1;\n"),
            golden.Insert(ProductVersion().Match(golden).Index, "DROP TABLE kernel.company;\n"),
            golden.TrimEnd() + "\nDROP TABLE kernel.company;\n",
        ];
        foreach (string mutated in mutations)
        {
            Assert.True(ProductVersion().IsMatch(mutated), "The mutation removed the history row the mask needs.");
            Assert.NotEqual(Normalise(golden), Normalise(mutated));
        }
    }

    [Fact]
    public void RR_137_record_mode_writes_a_missing_golden_file_as_generated_and_never_rewrites_an_existing_one()
    {
        // The same ReadGolden the RR-39 theory calls, so the record switch itself is under test, not a helper beside it.
        string directory = Directory.CreateTempSubdirectory("themis-golden-").FullName;
        try
        {
            string existing = Path.Combine(directory, "existing.sql");
            string merged = "INSERT INTO kernel.__ef_migrations_history (\"MigrationId\", \"ProductVersion\")\nVALUES ('20260101000000_Old', '10.0.13');\n";
            File.WriteAllText(existing, merged);
            string newer = "INSERT INTO kernel.__ef_migrations_history (\"MigrationId\", \"ProductVersion\")\r\nVALUES ('20260101000000_Old', '10.0.99');\r\n\r\n";

            Assert.Equal(merged, ReadGolden(directory, "existing", newer, record: true));
            Assert.Equal(merged, File.ReadAllText(existing));

            // Without record mode a missing file is not written; the theory fails on it instead.
            Assert.Throws<FileNotFoundException>(() => ReadGolden(directory, "missing", newer, record: false));
            Assert.False(File.Exists(Path.Combine(directory, "missing.sql")));

            // As generated: line endings and the trailing newline normalised, the real version kept, not the placeholder.
            string recorded = "INSERT INTO kernel.__ef_migrations_history (\"MigrationId\", \"ProductVersion\")\nVALUES ('20260101000000_Old', '10.0.99');\n";
            Assert.Equal(recorded, ReadGolden(directory, "missing", newer, record: true));
            Assert.Equal(recorded, File.ReadAllText(Path.Combine(directory, "missing.sql")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RR_39_every_golden_file_belongs_to_a_migration_the_assembly_holds()
    {
        HashSet<string> ids = KernelMigrations.All().ToHashSet(StringComparer.Ordinal);
        string[] stray = [.. Directory.GetFiles(GoldenDirectory(), "*.sql").Select(path => Path.GetFileNameWithoutExtension(path)).Where(name => !ids.Contains(name)).Order(StringComparer.Ordinal)];
        Assert.Empty(stray);
    }

    /// <summary>
    /// RR-39, RR-137: returns the golden file of a migration. In record mode it first writes the file when it does not
    /// exist yet (a new migration's), as generated: line endings and the trailing newline normalised, the product version
    /// not masked; the comparison masks it on both sides. A merged file is never rewritten under any EF version; an
    /// approved change, reviewed by the owner under RR-39, deletes the file and records it again.
    /// </summary>
    private static string ReadGolden(string directory, string migrationId, string script, bool record)
    {
        string path = Path.Combine(directory, migrationId + ".sql");
        if (record && !File.Exists(path))
        {
            File.WriteAllText(path, NormaliseLineEndings(script).TrimEnd() + "\n");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No golden script for {migrationId}; a new migration commits its own, generated with {RecordVariable}=1 (RR-39).", path);
        }

        return File.ReadAllText(path);
    }

    private static string NormaliseLineEndings(string script) => script.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Normalise(string script) =>
        ProductVersion().Replace(NormaliseLineEndings(script), "$1<product-version>$2").TrimEnd() + "\n";

    private static string FirstDifference(string expected, string actual)
    {
        string[] expectedLines = expected.Split('\n');
        string[] actualLines = actual.Split('\n');
        for (int i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            string left = i < expectedLines.Length ? expectedLines[i] : "<end>";
            string right = i < actualLines.Length ? actualLines[i] : "<end>";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return $"Line {i + 1}:{Environment.NewLine}  golden:    {left}{Environment.NewLine}  generated: {right}";
            }
        }

        return "No line differs; the scripts differ in length or whitespace.";
    }

    private static string GoldenDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Themis.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Themis.sln not found above the test output directory."), "db", "migrations", "golden");
    }
}

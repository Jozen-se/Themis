using System.Text.RegularExpressions;

namespace Themis.Architecture.Tests;

/// <summary>
/// RR-82: every regular expression in src anchors at the end of input with \z. A pattern that ends with $ also
/// matches before a final newline, so "ABC\n" would pass a Code check. The test reads every string literal on a line
/// that names a regular expression (GeneratedRegex, new Regex or a Regex field) and fails on one that ends with $.
/// </summary>
[Trait("Category", "Architecture")]
public sealed partial class RegexAnchorTests
{
    [GeneratedRegex(@"@""(?<verbatim>(?:[^""]|"""")*)""|""(?<regular>(?:[^""\\]|\\.)*)""")]
    private static partial Regex StringLiteral();

    [Fact]
    public void RR_82_no_regular_expression_in_src_ends_with_a_dollar_anchor()
    {
        string root = RepositoryRoot();
        string[] files = [.. Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))];

        List<string> offenders = [];
        int patterns = 0;
        foreach (string file in files)
        {
            foreach ((string pattern, int line) in Patterns(File.ReadAllLines(file)))
            {
                patterns++;
                if (EndsWithDollar(pattern))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line} \"{pattern}\"");
                }
            }
        }

        Assert.True(patterns >= 10, $"Only {patterns} regular expressions found in src; the scan looks broken.");
        Assert.True(offenders.Count == 0, $"Patterns anchored with $ instead of \\z (RR-82):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void RR_82_the_scan_finds_a_dollar_anchor_in_each_way_a_pattern_is_written()
    {
        string[] source =
        [
            "    [GeneratedRegex(\"^[A-Z]{3}$\")]",
            "    [GeneratedRegex(@\"^\\d{2}$\")]",
            "    private static readonly Regex Identifier = new(\"^[a-z]+$\", RegexOptions.CultureInvariant);",
            "    [GeneratedRegex(\"^[A-Z]{3}\\\\z\")]",
            "    [GeneratedRegex(@\"price \\$\")]",
        ];

        bool[] flagged = [.. Patterns(source).Select(found => EndsWithDollar(found.Pattern))];
        Assert.Equal([true, true, true, false, false], flagged);
    }

    /// <summary>The string literals of every line that builds or declares a regular expression, with their line number.</summary>
    private static IEnumerable<(string Pattern, int Line)> Patterns(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i].TrimStart();
            if (text.StartsWith("//", StringComparison.Ordinal) || !text.Contains("Regex", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match literal in StringLiteral().Matches(lines[i]))
            {
                yield return (literal.Groups["verbatim"].Success ? literal.Groups["verbatim"].Value : literal.Groups["regular"].Value.Replace(@"\\", @"\", StringComparison.Ordinal), i + 1);
            }
        }
    }

    /// <summary>A pattern ends with the $ anchor when its last character is $ and that $ is not escaped.</summary>
    private static bool EndsWithDollar(string pattern)
    {
        if (!pattern.EndsWith('$'))
        {
            return false;
        }

        int backslashes = pattern.Length - 1 - pattern[..^1].TrimEnd('\\').Length;
        return backslashes % 2 == 0;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Themis.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Themis.sln not found above the test output directory.");
    }
}

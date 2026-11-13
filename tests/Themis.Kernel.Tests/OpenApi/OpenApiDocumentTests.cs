using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Themis.Host;

namespace Themis.Kernel.Tests.OpenApi;

/// <summary>
/// RR-10 (Build Brief, milestone 2 step 4): the OpenAPI document is generated from code and committed as
/// openapi/v1.json. The test starts Themis.Host as Program builds it, in Development where the host maps the document,
/// with a connection string at a closed port and the relay and the purge off, so no database is reached; it reads
/// /openapi/v1.json and fails on any difference from the committed file (line endings and the trailing newline
/// normalised). A pull request that changes the API regenerates the file with THEMIS_OPENAPI_RECORD=1 and commits it;
/// the OpenAPI workflow then compares it with the base branch and fails on a breaking change unless the pull request
/// carries the api-breaking label (.github/workflows/openapi.yml, .github/scripts/openapi-check.sh).
/// </summary>
[Collection(HostTestGroup.Name)]
[Trait("Category", "Unit")]
public sealed class OpenApiDocumentTests
{
    private const string RecordVariable = "THEMIS_OPENAPI_RECORD";

    // A connection string registers persistence without connecting, so the endpoints are built as in production.
    private const string ConnectionString = "Host=localhost;Port=1;Database=themis;Username=themis_app;Password=none";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RR_10_openapi_v1_json_equals_the_document_the_host_generates()
    {
        string generated = Normalise(await GenerateAsync());
        string path = DocumentPath();
        if (Environment.GetEnvironmentVariable(RecordVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated);
        }

        Assert.True(File.Exists(path), $"openapi/v1.json is missing; generate it with {RecordVariable}=1 (RR-10, docs/development.md).");
        string committed = Normalise(File.ReadAllText(path));
        Assert.True(Difference(committed, generated) is null, $"openapi/v1.json is stale: it differs from the document Themis.Host generates. Regenerate it with {RecordVariable}=1 and commit it; CI then checks the change for breaking changes against the base branch (RR-10, docs/development.md).{Environment.NewLine}{Difference(committed, generated)}");
    }

    [Fact]
    public async Task RR_10_the_comparison_fails_on_a_stale_document_and_only_line_endings_and_the_trailing_newline_are_ignored()
    {
        string current = Normalise(await GenerateAsync());

        Assert.Null(Difference(current, Normalise(current.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n")));

        // Each mutation stands for a committed document that was not regenerated after a change in code.
        int firstPath = current.IndexOf("\"/v1/", StringComparison.Ordinal);
        Assert.True(firstPath > 0, "The generated document holds no /v1/ path.");
        string[] stale =
        [
            current.Remove(firstPath + 1, 1),
            current.Replace("\"/v1/", "\"/v2/", StringComparison.Ordinal),
            current.Insert(current.IndexOf('\n', StringComparison.Ordinal) + 1, "  \"x-stale\": true,\n"),
            current.TrimEnd('\n', '}') + ",\n  \"x-stale\": true\n}\n",
            current.Replace("\n", "\n ", StringComparison.Ordinal),
        ];
        foreach (string document in stale)
        {
            Assert.NotNull(Difference(Normalise(document), current));
        }
    }

    [Fact]
    public async Task RR_10_the_document_is_the_same_on_every_generation()
    {
        // A document that changed from one start to the next would make the RR-10 check fail at random.
        Assert.Equal(Normalise(await GenerateAsync()), Normalise(await GenerateAsync()));
    }

    [Fact]
    public async Task RR_10_RR_73_no_command_body_holds_a_route_value_or_the_computed_scope_and_a_command_with_nothing_left_in_its_body_takes_it_as_optional()
    {
        JsonNode document = JsonNode.Parse(await GenerateAsync())!;
        JsonObject schemas = document["components"]!["schemas"]!.AsObject();
        int commands = 0;
        int optional = 0;
        foreach ((string path, JsonNode? item) in document["paths"]!.AsObject())
        {
            JsonNode? post = item?["post"];
            bool isCommand = post?["parameters"]?.AsArray().Any(parameter => (string?)parameter?["name"] == "Idempotency-Key") == true;
            if (post?["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"] is not { } reference || !isCommand)
            {
                continue;
            }

            commands++;
            string name = ((string)reference!)["#/components/schemas/".Length..];
            JsonObject schema = schemas[name]!.AsObject();
            string[] properties = schema["properties"]?.AsObject().Select(property => property.Key).ToArray() ?? [];
            string[] required = schema["required"]?.AsArray().Select(value => (string)value!).ToArray() ?? [];
            string[] route = [.. path.Split('/').Where(segment => segment.StartsWith('{')).Select(segment => segment.Trim('{', '}').Split(':')[0])];

            // RR-73: the route wins over the body, so no body member names a route value, in any letter case.
            Assert.DoesNotContain(properties, property => route.Contains(property, StringComparer.OrdinalIgnoreCase));
            // The scope is computed from the command's data and the version travels in If-Match (R-API-02).
            Assert.DoesNotContain("scope", properties);
            Assert.DoesNotContain("expectedVersion", properties);
            Assert.All(required, member => Assert.Contains(member, properties));
            bool bodyRequired = (bool?)post["requestBody"]!["required"] ?? false;
            if (properties.Length == 0)
            {
                Assert.False(bodyRequired, $"POST {path} has nothing left in its body but still requires one (RR-10).");
            }

            if (!bodyRequired)
            {
                optional++;
            }
        }

        Assert.True(commands >= 19, $"Only {commands} command operations with a body schema were found.");
        Assert.True(optional >= 5, $"Only {optional} command operations take their body as optional.");
    }

    /// <summary>RR-10: starts Themis.Host through its own Program and reads the document it serves.</summary>
    private static async Task<string> GenerateAsync()
    {
        using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Themis", ConnectionString);
            builder.UseSetting("Outbox:Relay:Enabled", "false");
            builder.UseSetting("Idempotency:Purge:Enabled", "false");
            builder.UseSetting("Telemetry:ConsoleExporter", "false");
        });
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    private static string Normalise(string document) => document.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    /// <summary>The first line that differs, or null when the documents are equal character for character.</summary>
    private static string? Difference(string committed, string generated)
    {
        if (string.Equals(committed, generated, StringComparison.Ordinal))
        {
            return null;
        }

        string[] left = committed.Split('\n');
        string[] right = generated.Split('\n');
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            string a = i < left.Length ? left[i] : "<end>";
            string b = i < right.Length ? right[i] : "<end>";
            if (!string.Equals(a, b, StringComparison.Ordinal))
            {
                return $"Line {i + 1}:{Environment.NewLine}  committed: {a}{Environment.NewLine}  generated: {b}";
            }
        }

        return "No line differs; the documents differ in length or whitespace.";
    }

    private static string DocumentPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Themis.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Themis.sln not found above the test output directory."), "openapi", "v1.json");
    }
}

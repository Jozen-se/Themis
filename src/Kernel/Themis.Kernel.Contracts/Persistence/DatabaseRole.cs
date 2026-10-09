using System.Text.RegularExpressions;

namespace Themis.Kernel.Contracts.Persistence;

/// <summary>
/// A module's database role (D-26, D-41). The application connects as themis_app and switches to the owning module's
/// role with SET LOCAL ROLE before each statement that touches the module's schema (R-DB-01).
/// </summary>
public readonly partial record struct DatabaseRole
{
    private DatabaseRole(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public static DatabaseRole Kernel { get; } = new("themis_kernel");

    [GeneratedRegex("^themis_[a-z][a-z0-9_]{0,40}$")]
    private static partial Regex Pattern();

    public static DatabaseRole From(string name) =>
        Pattern().IsMatch(name) ? new DatabaseRole(name) : throw new ArgumentException($"'{name}' is not a Themis database role name.", nameof(name));

    public override string ToString() => Name;
}

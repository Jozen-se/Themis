using System.Text.RegularExpressions;

namespace Themis.Kernel.Contracts.Commands;

/// <summary>
/// Names a command: the permission code is TYPE.COMMAND, for example ITEM.CREATE or INVENTORY_ADJUSTMENT.POST
/// (R-SEC-01, Build Brief, Cross-cutting conventions). The type and verb also name the generated outbox event (D-44).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed partial class CommandAttribute : Attribute
{
    public CommandAttribute(string permissionCode)
    {
        Match match = Pattern().Match(permissionCode);
        if (!match.Success)
        {
            throw new ArgumentException($"'{permissionCode}' is not a TYPE.COMMAND permission code.", nameof(permissionCode));
        }

        PermissionCode = permissionCode;
        TypeCode = match.Groups["type"].Value;
        Verb = match.Groups["verb"].Value;
    }

    public string PermissionCode { get; }

    public string TypeCode { get; }

    public string Verb { get; }

    [GeneratedRegex("^(?<type>[A-Z][A-Z0-9_]{0,39})\\.(?<verb>[A-Z][A-Z0-9_]{0,39})$")]
    private static partial Regex Pattern();
}

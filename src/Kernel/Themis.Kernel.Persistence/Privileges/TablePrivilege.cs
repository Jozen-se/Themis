namespace Themis.Kernel.Persistence.Privileges;

/// <summary>A table privilege a module role may hold (brief, Database model; D-41, R-LED-06).</summary>
internal enum TablePrivilege
{
    Select,
    Insert,
    Update,
    Delete,
}

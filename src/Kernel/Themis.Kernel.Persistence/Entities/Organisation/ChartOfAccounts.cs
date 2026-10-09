namespace Themis.Kernel.Persistence.Entities.Organisation;

/// <summary>Phase 0 Specification, ChartOfAccounts: installation-level; companies may share one chart. Its accounts arrive with milestone 1 step 6.</summary>
internal sealed class ChartOfAccounts : MasterDataRow
{
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? Template { get; set; }
}

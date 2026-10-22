using Themis.Kernel.Contracts.Settings;

namespace Themis.Kernel.Settings;

/// <summary>
/// Definitions beside the catalogue, for tests only (docs/development/test-seams.md): the integration tests register
/// a history-bearing definition through it, since none of the milestone 2 definitions carries valid_from. Production
/// registers <see cref="NoSettingDefinitionSeam"/>, which adds nothing; the coverage test looks at the catalogue alone.
/// </summary>
internal interface ISettingDefinitionSeam
{
    IReadOnlyList<SettingDefinition> Definitions { get; }
}

internal sealed class NoSettingDefinitionSeam : ISettingDefinitionSeam
{
    public IReadOnlyList<SettingDefinition> Definitions { get; } = [];
}

/// <summary>The definitions this process knows: the catalogue, then the seam's; a key is looked up in that order.</summary>
internal sealed class SettingDefinitions
{
    private readonly Dictionary<string, SettingDefinition> _byKey;

    public SettingDefinitions(ISettingDefinitionSeam seam)
    {
        ArgumentNullException.ThrowIfNull(seam);
        _byKey = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        foreach (SettingDefinition definition in SettingCatalog.Definitions.Concat(seam.Definitions))
        {
            _byKey[definition.Key] = definition;
        }
    }

    public SettingDefinition? Find(string key) => _byKey.GetValueOrDefault(key);
}

using System.Text;

namespace Themis.Kernel.Organisation;

/// <summary>
/// The party duplicate key (R-MD-01): organisation number, VAT number, name and postal code, each normalised to
/// uppercase letters and digits only, joined with a bar. Two parties with the same key are the same real-world
/// entity until a user with the override permission says otherwise. The fields and threshold are a configurable
/// default (Foundation, R-MD-01); this is the normalisation only.
/// </summary>
internal static class DuplicateKeys
{
    public static string ForParty(string? organisationNumber, string? vatNumber, string name, string? postalCode) =>
        string.Join('|', Normalise(organisationNumber), Normalise(vatNumber), Normalise(name), Normalise(postalCode));

    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        foreach (char c in value.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

using Themis.Kernel.Primitives;

namespace Themis.Kernel.Contracts.Security;

/// <summary>
/// The system user: the User row that rows written by the system itself carry in created_by, updated_by and their
/// audit events, that is migrations and fixture seeds run by Themis.Migrator (R-AUD-01). Its subject can never be
/// issued by an identity provider, and access control refuses it on every request: the system user owns seeded
/// rows and never acts through the API (Build Brief, Authorisation).
/// </summary>
public static class WellKnownUsers
{
    public static UserId System { get; } = new(Guid.Parse("00000000-0000-7000-8000-000000000001"));

    /// <summary>The system user's external_subject; the colon keeps it outside every OpenID Connect provider's subject space.</summary>
    public const string SystemSubject = "themis:system";

    public const string SystemDisplayName = "Themis system";
}

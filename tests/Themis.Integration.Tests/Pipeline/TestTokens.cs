using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Themis.Kernel.Identity;

namespace Themis.Integration.Tests.Pipeline;

/// <summary>
/// The local test token issuer of the step 4 brief: a signing key generated per test run and tokens the tests mint
/// for any subject, so the real JWT bearer handler validates issuer, audience, signature and lifetime exactly as it
/// does for an OpenID Connect provider, without one running.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "https://test-issuer.themis.local";
    public const string Audience = "themis-api";

    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(48);
    private static readonly JsonWebTokenHandler Handler = new();

    public static string SigningKeyBase64 => Convert.ToBase64String(Key);

    /// <summary>A valid token for the subject, one hour long, optionally with claims Themis must ignore; a null subject leaves out the sub claim (RR-71).</summary>
    public static string For(string? subject, TimeProvider clock, IEnumerable<Claim>? extraClaims = null, string issuer = Issuer, string audience = Audience, TimeSpan? lifetime = null, byte[]? key = null)
    {
        DateTimeOffset now = clock.GetUtcNow();
        List<Claim> claims = subject is null ? [new Claim("name", "no subject")] : [new Claim("sub", subject)];
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        TimeSpan span = lifetime ?? TimeSpan.FromHours(1);
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime.AddMinutes(-1),
            IssuedAt = now.UtcDateTime,
            Expires = now.UtcDateTime.Add(span),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? Key), SecurityAlgorithms.HmacSha256),
        };
        return Handler.CreateToken(descriptor);
    }

    /// <summary>A token that expired an hour ago.</summary>
    public static string Expired(string subject, TimeProvider clock) => For(subject, clock, lifetime: TimeSpan.FromHours(-1));

    /// <summary>A token signed with a key the host does not know.</summary>
    public static string WrongKey(string subject, TimeProvider clock) => For(subject, clock, key: RandomNumberGenerator.GetBytes(48));
}

/// <summary>The users of the milestone 1 identity fixture (step 4 brief): the clerk may post, the preparer may not.</summary>
internal static class TestUsers
{
    public static Guid Clerk => Milestone1IdentityFixture.ClerkId;

    public static Guid Preparer => Milestone1IdentityFixture.PreparerId;

    public const string ClerkSubject = Milestone1IdentityFixture.ClerkSubject;

    public const string PreparerSubject = Milestone1IdentityFixture.PreparerSubject;

    /// <summary>The fixture user who holds SETTINGS_ADMINISTRATOR (RR-101); the clerk no longer does.</summary>
    public static Guid SettingsAdministrator => Milestone1IdentityFixture.SettingsAdministratorId;

    public const string SettingsAdministratorSubject = Milestone1IdentityFixture.SettingsAdministratorSubject;

    /// <summary>The fixture user who holds ORGANISATION_ADMINISTRATOR (milestone 2 step 3).</summary>
    public static Guid OrganisationAdministrator => Milestone1IdentityFixture.OrganisationAdministratorId;

    public const string OrganisationAdministratorSubject = Milestone1IdentityFixture.OrganisationAdministratorSubject;
}

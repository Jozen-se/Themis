namespace Themis.Kernel.Identity;

/// <summary>
/// The OpenID Connect provider the host trusts (D-12): Keycloak in development and self-hosting, any provider in
/// production. Tokens are validated against the provider's discovery document (issuer, audience, signature,
/// lifetime); only the subject is mapped. The test issuer replaces the provider with a local signing key for the
/// integration tests and is refused outside the Development and Test environments (step 4 brief).
/// </summary>
internal sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>The issuer, for example http://localhost:8080/realms/themis. Empty, with no test issuer, leaves the host without a signed-in subject: every command and query answers 401.</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The audience the token must carry: the API's client id, themis-api in the development realm.</summary>
    public string Audience { get; set; } = "themis-api";

    /// <summary>False only for the local stack, which serves the realm over plain HTTP.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public TestIssuerOptions TestIssuer { get; set; } = new();

    public bool HasTestIssuer => !string.IsNullOrWhiteSpace(TestIssuer.Issuer) || !string.IsNullOrWhiteSpace(TestIssuer.SigningKey);
}

/// <summary>A local issuer for tests: tokens signed with a shared key (HMAC SHA-256) by the test itself.</summary>
internal sealed class TestIssuerOptions
{
    /// <summary>The issuer the tokens name, for example https://test-issuer.themis.local.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The signing key, base64, at least 32 bytes.</summary>
    public string SigningKey { get; set; } = string.Empty;
}

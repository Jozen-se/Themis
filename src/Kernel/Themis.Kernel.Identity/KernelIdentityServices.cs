using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Identity;

/// <summary>Registers the identity capability. Called by the composition root only (and by the integration tests, with the test issuer).</summary>
internal static class KernelIdentityServices
{
    /// <summary>The authentication scheme every command and query is authenticated with: the standard JWT bearer handler (D-12).</summary>
    public const string Scheme = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>The environments in which the test issuer may be configured (step 4 brief).</summary>
    public static IReadOnlyList<string> TestIssuerEnvironments { get; } = ["Development", "Test", "Testing"];

    /// <summary>
    /// Bearer tokens from the configured OpenID Connect provider (section "Identity"), or from the test issuer in
    /// Development and Test. With neither configured the handler is not registered and the deny-all default stays:
    /// no subject, every command and query 401.
    /// </summary>
    public static IServiceCollection AddKernelIdentity(this IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        IdentityOptions options = new();
        configuration.GetSection(IdentityOptions.SectionName).Bind(options);

        if (options.HasTestIssuer)
        {
            // Step 4 brief: the host refuses to start with the test issuer configured outside Development and Test.
            if (!TestIssuerEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Identity:TestIssuer is configured in environment '{environmentName}'; the test issuer is allowed in {string.Join(", ", TestIssuerEnvironments)} only.");
            }

            return services.AddKernelIdentityWithTestIssuer(options.TestIssuer.Issuer, options.Audience, options.TestIssuer.SigningKey);
        }

        services.AddKernelAccessControl();
        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            return services;
        }

        return services.AddKernelIdentity(bearer =>
        {
            bearer.Authority = options.Authority;
            bearer.Audience = options.Audience;
            bearer.RequireHttpsMetadata = options.RequireHttpsMetadata;
        });
    }

    /// <summary>Bearer tokens signed with a local key for tests: issuer, audience, signature and lifetime are still validated.</summary>
    public static IServiceCollection AddKernelIdentityWithTestIssuer(this IServiceCollection services, string issuer, string audience, string signingKeyBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        byte[] key = Convert.FromBase64String(signingKeyBase64);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("Identity:TestIssuer:SigningKey holds at least 32 bytes, base64.");
        }

        return services.AddKernelIdentity(bearer =>
        {
            bearer.RequireHttpsMetadata = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        });
    }

    /// <summary>Bearer tokens validated as the caller configures: the provider's discovery document in the host, a local signing key in tests.</summary>
    public static IServiceCollection AddKernelIdentity(this IServiceCollection services, Action<JwtBearerOptions> configureBearer)
    {
        ArgumentNullException.ThrowIfNull(configureBearer);
        services.AddKernelAccessControl();
        services.AddHttpContextAccessor();
        services.AddAuthentication(Scheme).AddJwtBearer(Scheme, bearer =>
        {
            configureBearer(bearer);
            // Build Brief, Authorisation: the subject claim is the only claim mapped, under its own name.
            bearer.MapInboundClaims = false;
            bearer.TokenValidationParameters.NameClaimType = HttpSubject.SubjectClaim;
            bearer.TokenValidationParameters.RoleClaimType = "themis:roles-are-not-read-from-tokens";
            // RR-71: the 401 carries no token error details; the kernel's shared writer answers every challenge.
            bearer.IncludeErrorDetails = false;
            JwtBearerEvents events = bearer.Events ?? new JwtBearerEvents();
            Func<TokenValidatedContext, Task> validated = events.OnTokenValidated;
            events.OnTokenValidated = async context =>
            {
                await validated(context);
                // RR-71: a token without a sub claim is not a valid token here, so the request is anonymous and gets 401.
                if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst(HttpSubject.SubjectClaim)?.Value))
                {
                    context.Fail("The token carries no sub claim.");
                }
            };
            bearer.Events = events;
        });
        services.Replace(ServiceDescriptor.Scoped<ICurrentSubject, HttpSubject>());
        return services;
    }

    /// <summary>The database-backed principal resolution and authorisation (R-SEC-01), independent of how the subject arrives.</summary>
    public static IServiceCollection AddKernelAccessControl(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAccessControl, AccessControl>());
        return services;
    }
}

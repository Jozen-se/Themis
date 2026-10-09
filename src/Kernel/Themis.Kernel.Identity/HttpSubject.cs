using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Themis.Kernel.Contracts.Security;

namespace Themis.Kernel.Identity;

/// <summary>
/// The subject of the current HTTP request: the <c>sub</c> claim of the bearer token the JWT handler validated
/// against the configured issuer and audience. Nothing else from the token is read (Build Brief, Authorisation):
/// roles and email in the token are ignored, since permissions and scope live in the ERP (D-12).
/// </summary>
internal sealed class HttpSubject : ICurrentSubject
{
    public const string SubjectClaim = "sub";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpSubject(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public AuthenticatedSubject? Current
    {
        get
        {
            ClaimsPrincipal? user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity is not { IsAuthenticated: true })
            {
                return null;
            }

            string? subject = user.FindFirst(SubjectClaim)?.Value;
            return string.IsNullOrWhiteSpace(subject) ? null : new AuthenticatedSubject(subject);
        }
    }
}

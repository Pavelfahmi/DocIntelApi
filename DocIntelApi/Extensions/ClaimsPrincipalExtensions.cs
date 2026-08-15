using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DocIntelApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst("uid")?.Value
            ?? throw new UnauthorizedAccessException(
                "User ID claim not found in token.");

        return Guid.Parse(claim);
    }

    public static string GetEmail(this ClaimsPrincipal principal)
        => principal.FindFirst(ClaimTypes.Email)?.Value
           ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
           ?? string.Empty;

    public static bool IsAdmin(this ClaimsPrincipal principal)
        => principal.IsInRole("Admin")
           || principal.HasClaim(ClaimTypes.Role, "Admin")
           || principal.HasClaim("role", "Admin")
           || principal.HasClaim("is_admin", "true");
}
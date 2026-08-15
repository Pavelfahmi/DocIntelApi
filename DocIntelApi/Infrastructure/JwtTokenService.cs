using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DocIntelApi.Models.Entities;
using Microsoft.IdentityModel.Tokens;

namespace DocIntelApi.Infrastructure;

// Registered as Singleton — stateless, thread-safe, no DB access
// Just creates and validates tokens using the secret key
public interface IJwtTokenService
{
    string GenerateToken(User user);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;

    public JwtTokenService(IConfiguration config) => _config = config;

    public string GenerateToken(User user)
    {
        // Claims = pieces of data embedded inside the token
        // Anyone with the token can READ these (they're Base64, not encrypted)
        // But they CANNOT tamper with them without breaking the signature
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name,  user.FullName),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new Claim("uid", user.Id.ToString())
        };

        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim("role", "Admin"));
            claims.Add(new Claim("is_admin", "true"));
        }

        // The signing key — must be at least 32 characters (256 bits for HMAC-SHA256)
        // This key MUST be kept secret — anyone with it can forge tokens
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));

        // HMAC-SHA256 = signing algorithm
        // Creates a signature that proves the token came from us
        var credentials = new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256);

        var expiryMinutes = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

        // Build the actual token
        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        // Serialise to the three-part string: header.payload.signature
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
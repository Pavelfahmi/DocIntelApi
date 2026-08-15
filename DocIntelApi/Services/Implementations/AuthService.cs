using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Entities;
using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _config;

    public AuthService(
        AppDbContext db,
        IJwtTokenService jwt,
        IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _config = config;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        var email = request.Email.ToLowerInvariant();
        var exists = await _db.Users.AnyAsync(u => u.Email == email);

        if (exists)
            throw new InvalidOperationException(
                "A user with this email already exists.");

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(
            request.Password, workFactor: 12);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHash,
            FullName = request.FullName,
            IsAdmin = IsAdminEmail(email),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return BuildAuthResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email.ToLowerInvariant();
        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Email == email);

        if (user is null)
            throw new UnauthorizedAccessException("Invalid email or password.");

        var passwordValid = BCrypt.Net.BCrypt.Verify(
            request.Password, user.PasswordHash);

        if (!passwordValid)
            throw new UnauthorizedAccessException("Invalid email or password.");

        // Keep DB in sync if admin list in config changes
        var shouldBeAdmin = IsAdminEmail(email);
        if (user.IsAdmin != shouldBeAdmin)
        {
            user.IsAdmin = shouldBeAdmin;
            await _db.SaveChangesAsync();
        }

        return BuildAuthResponse(user);
    }

    private AuthResponse BuildAuthResponse(User user)
    {
        var token = _jwt.GenerateToken(user);
        var expiry = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

        return new AuthResponse(
            AccessToken: token,
            TokenType: "Bearer",
            ExpiresIn: expiry * 60,
            FullName: user.FullName,
            UserId: user.Id,
            IsAdmin: user.IsAdmin
        );
    }

    private bool IsAdminEmail(string email)
    {
        var admins = _config.GetSection("Admin:Emails").Get<string[]>() ?? [];
        return admins.Any(a =>
            string.Equals(a?.Trim(), email, StringComparison.OrdinalIgnoreCase));
    }
}

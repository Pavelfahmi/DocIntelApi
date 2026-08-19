using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;

namespace DocIntelApi.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);

    /// <summary>Reads <c>Users.IsAdmin</c> from the database (not JWT claims).</summary>
    Task<bool> IsAdminAsync(Guid userId, CancellationToken ct = default);
}
using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;

namespace DocIntelApi.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
}
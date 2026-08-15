namespace DocIntelApi.Models.Responses;

public record AuthResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string FullName,
    Guid UserId,
    bool IsAdmin
);

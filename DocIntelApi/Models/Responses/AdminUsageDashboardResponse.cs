namespace DocIntelApi.Models.Responses;

public record UserTokenUsageResponse(
    Guid UserId,
    string Email,
    string FullName,
    int AskCount,
    long TokensUsed
);

public record AdminUsageDashboardResponse(
    long TotalTokensUsed,
    long TokenBudget,
    long TokensRemaining,
    double PercentUsed,
    int TotalAsks,
    int TotalUsers,
    int UsersWithAsks,
    IReadOnlyList<UserTokenUsageResponse> ByUser
);

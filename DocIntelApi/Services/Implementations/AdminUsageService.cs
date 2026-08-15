using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Services.Implementations;

public class AdminUsageService : IAdminUsageService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AdminUsageService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AdminUsageDashboardResponse> GetDashboardAsync(
        CancellationToken ct = default)
    {
        var budget = _config.GetValue("Admin:TokenBudget", 1_000_000L);
        if (budget < 0) budget = 0;

        var totalUsed = await _db.ChatMessages.SumAsync(m => (long)m.TokensUsed, ct);
        var totalAsks = await _db.ChatMessages.CountAsync(ct);
        var totalUsers = await _db.Users.CountAsync(ct);

        var byUser = await _db.ChatMessages
            .AsNoTracking()
            .GroupBy(m => m.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                AskCount = g.Count(),
                TokensUsed = g.Sum(x => (long)x.TokensUsed)
            })
            .OrderByDescending(x => x.TokensUsed)
            .ToListAsync(ct);

        var userIds = byUser.Select(x => x.UserId).ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = byUser
            .Select(x =>
            {
                users.TryGetValue(x.UserId, out var u);
                return new UserTokenUsageResponse(
                    x.UserId,
                    u?.Email ?? "(deleted)",
                    u?.FullName ?? "(unknown)",
                    x.AskCount,
                    x.TokensUsed
                );
            })
            .ToList();

        var remaining = Math.Max(0, budget - totalUsed);
        var percent = budget <= 0
            ? 100
            : Math.Round(100.0 * totalUsed / budget, 2);

        return new AdminUsageDashboardResponse(
            TotalTokensUsed: totalUsed,
            TokenBudget: budget,
            TokensRemaining: remaining,
            PercentUsed: percent,
            TotalAsks: totalAsks,
            TotalUsers: totalUsers,
            UsersWithAsks: rows.Count,
            ByUser: rows
        );
    }
}

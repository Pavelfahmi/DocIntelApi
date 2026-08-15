using DocIntelApi.Models.Responses;

namespace DocIntelApi.Services.Interfaces;

public interface IAdminUsageService
{
    Task<AdminUsageDashboardResponse> GetDashboardAsync(CancellationToken ct = default);
}

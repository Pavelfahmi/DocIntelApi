using DocIntelApi.Extensions;
using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAdminUsageService _usage;

    public AdminController(AppDbContext db, IAdminUsageService usage)
    {
        _db = db;
        _usage = usage;
    }

    /// <summary>Org-wide token usage dashboard (admins only).</summary>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(AdminUsageDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        if (!await IsCurrentUserAdminAsync(ct))
            return Forbid();

        var dashboard = await _usage.GetDashboardAsync(ct);
        return Ok(dashboard);
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.IsAdmin)
            .FirstOrDefaultAsync(ct);
    }
}

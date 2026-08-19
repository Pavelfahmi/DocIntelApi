using DocIntelApi.Extensions;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocIntelApi.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IAdminUsageService _usage;

    public AdminController(IAuthService auth, IAdminUsageService usage)
    {
        _auth = auth;
        _usage = usage;
    }

    /// <summary>Org-wide token usage dashboard (admins only).</summary>
    [HttpGet("usage")]
    [ProducesResponseType(typeof(AdminUsageDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        var userId = User.GetUserId();
        if (!await _auth.IsAdminAsync(userId, ct))
            return Forbid();

        var dashboard = await _usage.GetDashboardAsync(ct);
        return Ok(dashboard);
    }
}

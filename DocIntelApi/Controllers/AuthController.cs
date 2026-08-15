using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocIntelApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]   // → api/v1/auth
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    // POST api/v1/auth/register
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // [ApiController] attribute automatically returns 400
        // if ModelState is invalid (validation attributes failed)
        // So we don't need: if (!ModelState.IsValid) return BadRequest()
        // That's one less thing compared to old Web API

        try
        {
            var result = await _auth.RegisterAsync(request);

            // 201 Created — standard for resource creation
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex)
        {
            // 409 Conflict — email already taken
            return Conflict(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    // POST api/v1/auth/login
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _auth.LoginAsync(request);
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            // 401 Unauthorized — wrong credentials
            return Unauthorized(new ProblemDetails
            {
                Title = "Login failed",
                Detail = ex.Message,
                Status = StatusCodes.Status401Unauthorized
            });
        }
    }
}
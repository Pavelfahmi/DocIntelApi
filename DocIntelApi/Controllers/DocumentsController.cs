using DocIntelApi.Extensions;
using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocIntelApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _docs;
    private readonly IRagService _rag;
    private readonly IAuthService _auth;

    public DocumentsController(
        IDocumentService docs,
        IRagService rag,
        IAuthService auth)
    {
        _docs = docs;
        _rag = rag;
        _auth = auth;
    }

    // POST api/v1/documents
    [HttpPost]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequest request)
    {
        var userId = User.GetUserId();
        var result = await _docs.UploadAsync(request, userId);

        return AcceptedAtAction(
            nameof(GetById),
            new { id = result.Id },
            result);
    }

    // GET api/v1/documents
    [HttpGet]
    [ProducesResponseType(typeof(DocumentListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var userId = User.GetUserId();
        var result = await _docs.GetAllAsync(userId);
        return Ok(result);
    }

    // GET api/v1/documents/{id}
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var userId = User.GetUserId();
        var result = await _docs.GetByIdAsync(id, userId);
        return result is null ? NotFound() : Ok(result);
    }

    // DELETE api/v1/documents/{id}
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = User.GetUserId();
        var deleted = await _docs.DeleteAsync(id, userId);
        return deleted ? NoContent() : NotFound();
    }

    // POST api/v1/documents/{id}/ask
    [HttpPost("{id:guid}/ask")]
    [ProducesResponseType(typeof(AskDocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ask(
        Guid id,
        [FromBody] AskDocumentRequest request,
        CancellationToken ct)
    {
        var userId = User.GetUserId();
        // Prefer DB flag — JWT role claim mapping can hide Admin on some setups
        var isAdmin = await _auth.IsAdminAsync(userId, ct);

        var result = await _rag.AskAsync(
            id,
            request,
            userId,
            includeUsage: isAdmin,
            ct);

        return result is null ? NotFound() : Ok(result);
    }
}

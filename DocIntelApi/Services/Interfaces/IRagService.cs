using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;

namespace DocIntelApi.Services.Interfaces;

public interface IRagService
{
    /// <summary>
    /// Answers a question using RAG over a document the user owns.
    /// Returns null if the document does not exist or is not owned by the user.
    /// Throws <see cref="InvalidOperationException"/> if the document is not Ready.
    /// Token usage is included only when <paramref name="includeUsage"/> is true (admins).
    /// </summary>
    Task<AskDocumentResponse?> AskAsync(
        Guid documentId,
        AskDocumentRequest request,
        Guid userId,
        bool includeUsage = false,
        CancellationToken ct = default);
}

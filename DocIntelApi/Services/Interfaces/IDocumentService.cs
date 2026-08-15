using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;

namespace DocIntelApi.Services.Interfaces;

public interface IDocumentService
{
    Task<DocumentResponse> UploadAsync(
        UploadDocumentRequest request, Guid userId);

    Task<DocumentListResponse> GetAllAsync(Guid userId);

    Task<DocumentResponse?> GetByIdAsync(Guid id, Guid userId);

    Task<bool> DeleteAsync(Guid id, Guid userId);
}
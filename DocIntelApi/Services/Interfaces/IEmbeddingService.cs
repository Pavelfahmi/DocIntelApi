namespace DocIntelApi.Services.Interfaces;

public interface IEmbeddingService
{
    Task IndexDocumentAsync(Guid documentId, CancellationToken ct = default);
}

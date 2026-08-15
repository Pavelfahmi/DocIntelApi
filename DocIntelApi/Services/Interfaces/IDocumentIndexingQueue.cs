namespace DocIntelApi.Services.Interfaces;

public interface IDocumentIndexingQueue
{
    ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}

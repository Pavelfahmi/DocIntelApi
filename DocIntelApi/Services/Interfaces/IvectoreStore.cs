namespace DocIntelApi.Services.Interfaces;

public record VectorSearchResult(
    string ChunkText,
    int ChunkIndex,
    double Score        // similarity score 0-1, higher = more relevant
);

public interface IVectorStore
{
    // Creates a collection in Qdrant for this document
    Task CreateCollectionAsync(
        string collectionName, int vectorSize, CancellationToken ct = default);

    // Stores a batch of vectors with their text payloads
    Task UpsertAsync(
        string collectionName,
        IEnumerable<(float[] Vector, string Text, int Index)> points,
        CancellationToken ct = default);

    // Finds the most similar vectors to the query vector
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName,
        float[] queryVector,
        int topK = 5,
        CancellationToken ct = default);

    // Removes all vectors for a document (called on document delete)
    Task DeleteCollectionAsync(
        string collectionName, CancellationToken ct = default);

    // Check if a collection already exists
    Task<bool> CollectionExistsAsync(
        string collectionName, CancellationToken ct = default);
}
using DocIntelApi.Services.Interfaces;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace DocIntelApi.Services.Implementations;

public class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;

    public QdrantVectorStore(
        QdrantClient client,
        ILogger<QdrantVectorStore> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task CreateCollectionAsync(
      string collectionName, int vectorSize, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating Qdrant collection: {Collection}", collectionName);

        await _client.CreateCollectionAsync(
            collectionName,
            new VectorParams
            {
                Size = (ulong)vectorSize,
                Distance = Distance.Cosine
            },
            cancellationToken: ct);
    }

    public async Task UpsertAsync(
        string collectionName,
        IEnumerable<(float[] Vector, string Text, int Index)> points,
        CancellationToken ct = default)
    {
        // Convert our tuples to Qdrant PointStruct format
        var qdrantPoints = points.Select((p, i) => new PointStruct
        {
            // Unique ID for this vector point
            Id = new PointId { Num = (ulong)p.Index },

            // The actual embedding vector
            Vectors = new Vectors { Vector = new Vector { Data = { p.Vector } } },

            // Payload = metadata stored alongside the vector
            // We store the text so we can return it in search results
            // without needing to look it up in PostgreSQL
            Payload =
            {
                ["text"]        = p.Text,
                ["chunk_index"] = p.Index
            }
        }).ToList();

        await _client.UpsertAsync(collectionName, qdrantPoints,
            cancellationToken: ct);

        _logger.LogInformation(
            "Upserted {Count} vectors into {Collection}",
            qdrantPoints.Count, collectionName);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collectionName,
        float[] queryVector,
        int topK = 5,
        CancellationToken ct = default)
    {
        var results = await _client.SearchAsync(
            collectionName,
            queryVector,
            limit: (ulong)topK,
            cancellationToken: ct);

        return results.Select(r => new VectorSearchResult(
            ChunkText: r.Payload["text"].StringValue,
            ChunkIndex: (int)r.Payload["chunk_index"].IntegerValue,
            Score: r.Score
        )).ToList();
    }

    public async Task DeleteCollectionAsync(
        string collectionName, CancellationToken ct = default)
    {
        var exists = await CollectionExistsAsync(collectionName, ct);
        if (!exists) return;

        await _client.DeleteCollectionAsync(collectionName,
            cancellationToken: ct);

        _logger.LogInformation(
            "Deleted Qdrant collection: {Collection}", collectionName);
    }

    public async Task<bool> CollectionExistsAsync(
        string collectionName, CancellationToken ct = default)
    {
        var collections = await _client.ListCollectionsAsync(ct);
        return collections.Any(c => c == collectionName);
    }
}
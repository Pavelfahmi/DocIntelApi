using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Entities;
using DocIntelApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Services.Implementations;

public class EmbeddingService : IEmbeddingService
{
    private readonly AppDbContext _db;
    private readonly ILLMProvider _llm;
    private readonly IVectorStore _vectorStore;
    private readonly ITextChunkingService _chunker;
    private readonly ILogger<EmbeddingService> _logger;

    // Qdrant collection name per document — keeps each document isolated
    // Pattern: "doc-{documentId}"
    private static string CollectionName(Guid id) => $"doc-{id}";

    public EmbeddingService(
        AppDbContext db,
        ILLMProvider llm,
        IVectorStore vectorStore,
        ITextChunkingService chunker,
        ILogger<EmbeddingService> logger)
    {
        _db = db;
        _llm = llm;
        _vectorStore = vectorStore;
        _chunker = chunker;
        _logger = logger;
    }

    public async Task IndexDocumentAsync(
        Guid documentId, CancellationToken ct = default)
    {
        // Load document from PostgreSQL
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document is null)
        {
            _logger.LogWarning(
                "Document {Id} not found for indexing", documentId);
            return;
        }

        try
        {
            // Update status → Processing
            document.Status = DocumentStatus.Processing;
            await _db.SaveChangesAsync(ct);

            // Chunk the extracted text
            var chunks = _chunker.Chunk(document.ExtractedText);

            _logger.LogInformation(
                "Indexing {ChunkCount} chunks for document {Id}",
                chunks.Count, documentId);

            // Create a Qdrant collection for this document
            // We embed one chunk first to know the vector size
            var sampleVector = await _llm.EmbedAsync(chunks[0], ct);
            var vectorSize = sampleVector.Length; // gemini-embedding-001 default is often 3072

            var collectionName = CollectionName(documentId);

            // Delete existing collection if re-indexing
            await _vectorStore.DeleteCollectionAsync(collectionName, ct);
            await _vectorStore.CreateCollectionAsync(
                collectionName, vectorSize, ct);

            // Embed all chunks and collect vectors
            // We process in batches to avoid hitting rate limits
            var points = new List<(float[] Vector, string Text, int Index)>();

            for (int i = 0; i < chunks.Count; i++)
            {
                _logger.LogDebug(
                    "Embedding chunk {Current}/{Total}", i + 1, chunks.Count);

                var vector = i == 0
                    ? sampleVector          // reuse the first one we already embedded
                    : await _llm.EmbedAsync(chunks[i], ct);

                points.Add((vector, chunks[i], i));

                // Small delay every 10 chunks to respect rate limits
                // Gemini free tier: 1500 requests/day, ~1/second burst
                if (i > 0 && i % 10 == 0)
                    await Task.Delay(500, ct);
            }

            // Store all vectors in Qdrant
            await _vectorStore.UpsertAsync(collectionName, points, ct);

            // Update document status → Ready
            document.Status = DocumentStatus.Ready;
            document.ChunkCount = chunks.Count;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Document {Id} indexed successfully. {Count} vectors stored.",
                documentId, chunks.Count);
        }
        catch (Exception ex)
        {
            // Update status → Failed so user knows something went wrong
            document.Status = DocumentStatus.Failed;
            await _db.SaveChangesAsync(ct);

            _logger.LogError(ex,
                "Failed to index document {Id}", documentId);

            throw;
        }
    }
}
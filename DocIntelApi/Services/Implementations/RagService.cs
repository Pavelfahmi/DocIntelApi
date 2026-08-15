using System.Text;
using System.Text.Json;
using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Entities;
using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Services.Implementations;

public class RagService : IRagService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    private readonly ILLMProvider _llm;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<RagService> _logger;

    private static string CollectionName(Guid id) => $"doc-{id}";

    public RagService(
        AppDbContext db,
        ILLMProvider llm,
        IVectorStore vectorStore,
        ILogger<RagService> logger)
    {
        _db = db;
        _llm = llm;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<AskDocumentResponse?> AskAsync(
        Guid documentId,
        AskDocumentRequest request,
        Guid userId,
        bool includeUsage = false,
        CancellationToken ct = default)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);

        if (document is null)
            return null;

        if (document.Status != DocumentStatus.Ready)
            throw new InvalidOperationException(
                $"Document is not ready for questions (status: {document.Status}). " +
                "Wait until indexing completes.");

        var collectionName = CollectionName(documentId);
        if (!await _vectorStore.CollectionExistsAsync(collectionName, ct))
            throw new InvalidOperationException(
                "Vector index is missing for this document. Re-upload or re-index it.");

        var question = request.Question.Trim();
        var topK = request.TopK;

        _logger.LogInformation(
            "RAG ask on document {DocumentId}: topK={TopK}", documentId, topK);

        var queryVector = await _llm.EmbedAsync(
            question, ct, taskType: "RETRIEVAL_QUERY");
        var hits = await _vectorStore.SearchAsync(
            collectionName, queryVector, topK, ct);

        if (hits.Count == 0)
            throw new InvalidOperationException(
                "No relevant passages found in this document.");

        var context = BuildContext(hits);
        var prompt = BuildPrompt(question, context);
        var completion = await _llm.CompleteAsync(prompt, ct);

        var sources = hits
            .Select(h => new SourceChunkResponse(h.ChunkIndex, h.Score, h.ChunkText))
            .ToList();

        var message = new ChatMessage
        {
            Id = Guid.NewGuid(),
            Question = question,
            Answer = completion.Text,
            SourceChunksJson = JsonSerializer.Serialize(sources, JsonOptions),
            TokensUsed = completion.TotalTokens,
            CreatedAt = DateTimeOffset.UtcNow,
            DocumentId = documentId,
            UserId = userId
        };

        _db.ChatMessages.Add(message);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RAG answer saved as message {MessageId} for document {DocumentId}. Tokens={Tokens}",
            message.Id, documentId, completion.TotalTokens);

        TokenUsageResponse? usage = includeUsage
            ? new TokenUsageResponse(
                completion.PromptTokens,
                completion.OutputTokens,
                completion.TotalTokens)
            : null;

        return new AskDocumentResponse(
            MessageId: message.Id,
            DocumentId: documentId,
            Question: question,
            Answer: completion.Text,
            Sources: sources,
            CreatedAt: message.CreatedAt,
            Usage: usage
        );
    }

    private static string BuildContext(IReadOnlyList<VectorSearchResult> hits)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < hits.Count; i++)
        {
            sb.AppendLine($"[Passage {i + 1} | score={hits[i].Score:F3}]");
            sb.AppendLine(hits[i].ChunkText);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildPrompt(string question, string context) =>
        $"""
        You are a helpful assistant that answers questions using ONLY the document passages below.
        If the answer is not in the passages, say you cannot find it in the document.
        Do not invent facts. Keep answers concise and clear.

        Document passages:
        ---
        {context}
        ---
        Question: {question}

        Answer:
        """;
}

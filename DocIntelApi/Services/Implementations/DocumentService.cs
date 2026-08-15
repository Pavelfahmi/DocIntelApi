using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Entities;
using DocIntelApi.Models.Requests;
using DocIntelApi.Models.Responses;
using DocIntelApi.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Services.Implementations;

public class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly IDocumentTextExtractor _extractor;
    private readonly ITextChunkingService _chunker;
    private readonly IDocumentIndexingQueue _indexingQueue;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<DocumentService> _logger;

    private static string CollectionName(Guid id) => $"doc-{id}";

    public DocumentService(
        AppDbContext db,
        IDocumentTextExtractor extractor,
        ITextChunkingService chunker,
        IDocumentIndexingQueue indexingQueue,
        IVectorStore vectorStore,
        ILogger<DocumentService> logger)
    {
        _db = db;
        _extractor = extractor;
        _chunker = chunker;
        _indexingQueue = indexingQueue;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<DocumentResponse> UploadAsync(
        UploadDocumentRequest request, Guid userId)
    {
        var fileName = request.File.FileName;

        if (!_extractor.CanExtract(fileName))
        {
            throw new InvalidOperationException(
                "Unsupported file type. Supported: PDF, DOCX, TXT, MD, CSV, JSON, XML, HTML, RTF, LOG. (Legacy .doc → save as .docx)");
        }

        const long maxBytes = 10 * 1024 * 1024;
        if (request.File.Length > maxBytes)
            throw new InvalidOperationException(
                "File size cannot exceed 10MB.");

        _logger.LogInformation(
            "Processing upload: {FileName} for user {UserId}",
            fileName, userId);

        string extractedText;
        try
        {
            await using var stream = request.File.OpenReadStream();
            extractedText = _extractor.ExtractText(stream, fileName);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Text extraction failed for {FileName}", fileName);
            throw new InvalidOperationException(
                $"Could not extract text from '{fileName}'. The file may be corrupt, password-protected, or image-only.");
        }

        if (string.IsNullOrWhiteSpace(extractedText))
            throw new InvalidOperationException(
                "Could not extract text from the document. " +
                "It may be empty, scanned, or image-based (OCR not enabled).");

        var chunks = _chunker.Chunk(extractedText);

        _logger.LogInformation(
            "Document split into {ChunkCount} chunks", chunks.Count);

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ExtractedText = extractedText,
            Status = DocumentStatus.Pending,
            ChunkCount = chunks.Count,
            UserId = userId,
            UploadedAt = DateTimeOffset.UtcNow
        };

        _db.Documents.Add(document);
        await _db.SaveChangesAsync();

        await _indexingQueue.EnqueueAsync(document.Id);

        _logger.LogInformation(
            "Document {DocumentId} saved and queued for indexing", document.Id);

        return MapToResponse(document);
    }

    public async Task<DocumentListResponse> GetAllAsync(Guid userId)
    {
        var documents = await _db.Documents
            .Where(d => d.UserId == userId)
            .AsNoTracking()
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();

        var items = documents.Select(MapToResponse).ToList();
        return new DocumentListResponse(items, items.Count);
    }

    public async Task<DocumentResponse?> GetByIdAsync(Guid id, Guid userId)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        return document is null ? null : MapToResponse(document);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document is null) return false;

        _db.Documents.Remove(document);
        await _db.SaveChangesAsync();

        try
        {
            await _vectorStore.DeleteCollectionAsync(CollectionName(id));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete Qdrant collection for document {DocumentId}", id);
        }

        _logger.LogInformation("Document {DocumentId} deleted", id);
        return true;
    }

    private static DocumentResponse MapToResponse(Document d) => new(
        Id: d.Id,
        FileName: d.FileName,
        Status: d.Status.ToString(),
        ChunkCount: d.ChunkCount,
        UploadedAt: d.UploadedAt
    );
}

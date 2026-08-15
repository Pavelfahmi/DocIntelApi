using DocIntelApi.Services.Interfaces;

namespace DocIntelApi.Services.Implementations;

/// <summary>
/// Pulls document IDs from the queue and indexes them in a fresh DI scope
/// (safe DbContext / HttpClient lifetimes).
/// </summary>
public sealed class DocumentIndexingBackgroundService : BackgroundService
{
    private readonly IDocumentIndexingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentIndexingBackgroundService> _logger;

    public DocumentIndexingBackgroundService(
        IDocumentIndexingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentIndexingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document indexing background service started");

        await foreach (var documentId in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var embedding = scope.ServiceProvider.GetRequiredService<IEmbeddingService>();
                await embedding.IndexDocumentAsync(documentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Background indexing failed for document {DocumentId}", documentId);
            }
        }
    }
}

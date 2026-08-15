using System.Threading.Channels;
using DocIntelApi.Services.Interfaces;

namespace DocIntelApi.Services.Implementations;

/// <summary>
/// In-memory queue for document indexing jobs.
/// Survives beyond the HTTP request so scoped DbContext is not used after disposal.
/// </summary>
public sealed class DocumentIndexingQueue : IDocumentIndexingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(documentId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}

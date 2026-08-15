namespace DocIntelApi.Services.Implementations;

public interface ITextChunkingService
{
    IReadOnlyList<string> Chunk(string text, int chunkSize = 500, int overlap = 100);
}

public class TextChunkingService : ITextChunkingService
{
    // Splits text into overlapping word-based chunks
    // chunkSize = words per chunk
    // overlap   = words shared between consecutive chunks
    // Why overlap? A sentence can span a chunk boundary —
    // overlap ensures neither chunk loses that context
    public IReadOnlyList<string> Chunk(
        string text,
        int chunkSize = 500,
        int overlap = 100)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Split into words — simple but effective for most documents
        var words = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var chunks = new List<string>();
        var step = chunkSize - overlap;  // how far we advance each iteration

        for (int i = 0; i < words.Length; i += step)
        {
            // Take up to chunkSize words starting at position i
            var chunkWords = words
                .Skip(i)
                .Take(chunkSize)
                .ToArray();

            // Join back to a readable string
            chunks.Add(string.Join(" ", chunkWords));

            // Stop if we've passed the end
            if (i + chunkSize >= words.Length)
                break;
        }

        return chunks;
    }
}
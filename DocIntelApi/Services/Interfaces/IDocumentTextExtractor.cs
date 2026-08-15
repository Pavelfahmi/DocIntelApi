namespace DocIntelApi.Services.Interfaces;

public interface IDocumentTextExtractor
{
    /// <summary>True if this file name/extension is supported.</summary>
    bool CanExtract(string fileName);

    /// <summary>Extract plain text for chunking/embedding.</summary>
    string ExtractText(Stream stream, string fileName);
}

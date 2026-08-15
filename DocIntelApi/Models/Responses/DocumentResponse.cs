namespace DocIntelApi.Models.Responses;

public record DocumentResponse(
    Guid Id,
    string FileName,
    string Status,       // "Pending" | "Processing" | "Ready" | "Failed"
    int ChunkCount,
    DateTimeOffset UploadedAt
);

public record DocumentListResponse(
    IEnumerable<DocumentResponse> Items,
    int TotalCount
);
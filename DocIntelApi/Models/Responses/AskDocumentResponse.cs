namespace DocIntelApi.Models.Responses;

public record SourceChunkResponse(
    int ChunkIndex,
    double Score,
    string Text
);

public record TokenUsageResponse(
    int PromptTokens,
    int OutputTokens,
    int TotalTokens
);

public record AskDocumentResponse(
    Guid MessageId,
    Guid DocumentId,
    string Question,
    string Answer,
    IReadOnlyList<SourceChunkResponse> Sources,
    DateTimeOffset CreatedAt,
    /// <summary>Present only for admin users.</summary>
    TokenUsageResponse? Usage = null
);

using System;

namespace DocIntelApi.Models.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }

    // The question the user typed
    public required string Question { get; set; }

    // The answer the LLM returned
    public required string Answer { get; set; }

    // Which document chunks were used to build this answer
    // Stored as JSON string — we serialise/deserialise in the service layer
    // Example: [{"text":"...","page":1,"score":0.91}]
    public string SourceChunksJson { get; set; } = "[]";

    // Tracks LLM token consumption per message
    // Critical for staying within free tier rate limits
    public int TokensUsed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Foreign keys
    public Guid DocumentId { get; set; }
    public Guid UserId { get; set; }

    // Navigation properties
    public Document Document { get; set; } = null!;
    public User User { get; set; } = null!;
}
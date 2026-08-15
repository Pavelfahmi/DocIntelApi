using System;
using System.Collections.Generic;

namespace DocIntelApi.Models.Entities;

public class Document
{
    public Guid Id { get; set; }

    public required string FileName { get; set; }

    // Original text extracted from the uploaded PDF
    // Stored so we can re-chunk or re-embed without re-uploading
    public required string ExtractedText { get; set; }

    // Tracks where this document is in our processing pipeline
    // Pending → Processing → Ready → Failed
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    // How many text chunks we split this document into
    // Each chunk becomes one vector in Qdrant
    public int ChunkCount { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    // Foreign key — which user uploaded this document
    public Guid UserId { get; set; }

    // Navigation property — back reference to the owner
    // null! tells the compiler: "EF Core will always populate this,
    // trust me it won't be null at runtime"
    public User User { get; set; } = null!;

    // One Document has many chat conversations
    public ICollection<ChatMessage> ChatMessages { get; set; } = [];
}

// Stored as string in PostgreSQL ("Ready" not 2) — readable in DB tools
public enum DocumentStatus
{
    Pending = 0,
    Processing = 1,
    Ready = 2,
    Failed = 3
}
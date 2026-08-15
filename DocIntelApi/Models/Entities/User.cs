using System;
using System.Collections.Generic;

namespace DocIntelApi.Models.Entities;

public class User
{
    // Guid primary key — not guessable, safe to expose in APIs
    // PostgreSQL will auto-generate this via gen_random_uuid()
    public Guid Id { get; set; }

    // required = compiler enforces this is set on creation
    // NOT NULL in the database
    public required string Email { get; set; }

    // Never store plain text passwords — always a hashed value
    public required string PasswordHash { get; set; }

    public required string FullName { get; set; }

    /// <summary>Admins can see LLM token usage in Ask responses.</summary>
    public bool IsAdmin { get; set; }

    // DateTimeOffset includes timezone info
    // Always use this over DateTime in APIs — avoids timezone bugs
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation property — EF Core uses this to build JOINs
    // One User owns many Documents
    // = [] initialises to empty list — avoids null reference errors
    public ICollection<Document> Documents { get; set; } = [];
}
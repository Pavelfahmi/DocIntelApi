using DocIntelApi.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi.Infrastructure;

// DbContext = the bridge between C# objects and the PostgreSQL database
// It tracks changes, manages connections, and translates LINQ to SQL
// Registered as Scoped — one instance per HTTP request (never Singleton)
public class AppDbContext : DbContext
{
    // DbContextOptions injected via DI — carries the connection string
    // and tells EF Core to use Npgsql (PostgreSQL) provider
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    // DbSet<T> = represents a table in PostgreSQL
    // Use Set<T>() pattern — safer than auto-property in EF Core 10
    public DbSet<User> Users => Set<User>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // OnModelCreating = configure tables, constraints, and relationships
    // Runs ONCE at startup when EF Core builds its internal model
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── USER ────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            // gen_random_uuid() is a native PostgreSQL function
            // Generates a UUID automatically on INSERT if not provided
          

            // No two users can share the same email
            entity.HasIndex(u => u.Email)
                  .IsUnique();

            entity.Property(u => u.Email)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(u => u.FullName)
                  .IsRequired()
                  .HasMaxLength(100);

            // PasswordHash can be long (bcrypt produces ~60 chars)
            entity.Property(u => u.PasswordHash)
                  .IsRequired()
                  .HasMaxLength(256);

            entity.Property(u => u.IsAdmin)
                  .HasDefaultValue(false);
        });

        // ── DOCUMENT ─────────────────────────────────────────────
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);

          

            entity.Property(d => d.FileName)
                  .IsRequired()
                  .HasMaxLength(256);

            // Store enum as string ("Ready") not int (2)
            // Much more readable when querying the DB directly
            entity.Property(d => d.Status)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            // ExtractedText can be very large — no max length
            entity.Property(d => d.ExtractedText)
                  .IsRequired();

            // Relationship: One User → Many Documents
            // Cascade: if User is deleted → their Documents are deleted too
            entity.HasOne(d => d.User)
                  .WithMany(u => u.Documents)
                  .HasForeignKey(d => d.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CHATMESSAGE ──────────────────────────────────────────
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(c => c.Id);

           

            entity.Property(c => c.Question)
                  .IsRequired();

            entity.Property(c => c.Answer)
                  .IsRequired();

            // SourceChunksJson defaults to empty JSON array
            entity.Property(c => c.SourceChunksJson)
                  .HasDefaultValue("[]");

            // Relationship: One Document → Many ChatMessages
            // Cascade: if Document deleted → its ChatMessages deleted too
            entity.HasOne(c => c.Document)
                  .WithMany(d => d.ChatMessages)
                  .HasForeignKey(c => c.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relationship: ChatMessage belongs to a User
            // NoAction: we don't cascade delete from User → ChatMessage
            // because Document cascade already handles cleanup
            entity.HasOne(c => c.User)
                  .WithMany()
                  .HasForeignKey(c => c.UserId)
                  .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
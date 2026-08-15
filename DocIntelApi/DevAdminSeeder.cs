using DocIntelApi.Infrastructure;
using DocIntelApi.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocIntelApi;

/// <summary>
/// Development-only helper: ensures a hardcoded admin user exists in the DB.
/// </summary>
public static class DevAdminSeeder
{
    public const string DefaultEmail = "admin@docintel.local";
    public const string DefaultPassword = "Admin123!";
    public const string DefaultName = "System Owner";

    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DevAdminSeeder");

        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not apply migrations automatically. Ensuring IsAdmin column exists…");
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsAdmin" boolean NOT NULL DEFAULT false;""");
        }

        // Safety net if migration history is out of sync
        await db.Database.ExecuteSqlRawAsync(
            """ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsAdmin" boolean NOT NULL DEFAULT false;""");


        var email = DefaultEmail.ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FullName = DefaultName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword, workFactor: 12),
                IsAdmin = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            logger.LogInformation(
                "Created default admin: {Email} / {Password}", email, DefaultPassword);
            return;
        }

        var changed = false;
        if (!user.IsAdmin)
        {
            user.IsAdmin = true;
            changed = true;
            logger.LogInformation("Promoted existing user to admin: {Email}", email);
        }

        if (user.FullName != DefaultName)
        {
            user.FullName = DefaultName;
            changed = true;
        }

        if (changed)
            await db.SaveChangesAsync();
    }
}

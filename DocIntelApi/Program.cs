using System.Text;
using DocIntelApi;
using DocIntelApi.Infrastructure;
using DocIntelApi.Services.Implementations;
using DocIntelApi.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Qdrant.Client;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── SERVICES ─────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"))
    .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
);

// JWT Token Service — Singleton because it's stateless
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// Auth Service — Scoped because it uses DbContext
builder.Services.AddScoped<IAuthService, AuthService>();

// Document processing services
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IDocumentTextExtractor, DocumentTextExtractor>();
builder.Services.AddSingleton<ITextChunkingService, TextChunkingService>();

// HTTP client for Gemini API calls
builder.Services.AddHttpClient<GeminiProvider>();

// LLM Provider — swap this one line to change LLM
builder.Services.AddScoped<ILLMProvider, GeminiProvider>();

// Vector store
builder.Services.AddScoped<IVectorStore, QdrantVectorStore>();

// Embedding orchestration
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();

// RAG Q&A over indexed documents
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddScoped<IAdminUsageService, AdminUsageService>();

// Background indexing — queue is singleton; worker creates a scope per job
builder.Services.AddSingleton<IDocumentIndexingQueue, DocumentIndexingQueue>();
builder.Services.AddHostedService<DocumentIndexingBackgroundService>();

// Qdrant client — Singleton, maintains gRPC connection pool
builder.Services.AddSingleton<QdrantClient>(_ =>
    new QdrantClient(
        host: builder.Configuration["Qdrant:Host"] ?? "localhost",
        port: int.Parse(builder.Configuration["Qdrant:Port"] ?? "6334"),
        https: false
    )
);

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Jwt:Secret is missing. Set it via user-secrets or environment variables.");

if (jwtSecret.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 characters.");

if (string.IsNullOrWhiteSpace(builder.Configuration["Gemini:ApiKey"]))
    throw new InvalidOperationException(
        "Gemini:ApiKey is missing. Set it with: " +
        "dotnet user-secrets set \"Gemini:ApiKey\" \"YOUR_KEY\"");

// Configure JWT Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)),

            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");
                logger.LogWarning(ctx.Exception,
                    "JWT authentication failed: {Message}", ctx.Exception.Message);
                return Task.CompletedTask;
            },
            OnChallenge = ctx =>
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");
                logger.LogWarning(
                    "JWT challenge (401). Error={Error} Desc={Description}",
                    ctx.Error, ctx.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ── BUILD ─────────────────────────────────────────────────────────────
var app = builder.Build();

// ── MIDDLEWARE PIPELINE ───────────────────────────────────────────────
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

if (!string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase))
    app.UseHttpsRedirection();

// SPA UI (wwwroot) — same origin as API, no CORS needed
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── ENDPOINTS ─────────────────────────────────────────────────────────
app.MapControllers();
app.MapOpenApi();
app.MapScalarApiReference();

if (app.Environment.IsDevelopment())
    await DevAdminSeeder.SeedAsync(app);

app.Run();

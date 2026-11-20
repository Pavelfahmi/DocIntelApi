using System.Security.Cryptography;
using System.Text;
using DocIntelApi;
using DocIntelApi.Hosting;
using DocIntelApi.Infrastructure;
using DocIntelApi.Middleware;
using DocIntelApi.Services.Implementations;
using DocIntelApi.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Qdrant.Client;
using Scalar.AspNetCore;
using Serilog;

// Capture early startup failures before the host is built
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    try
    {
        Directory.CreateDirectory("logs");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not create logs directory; file sink may be unavailable");
    }

    var builder = WebApplication.CreateBuilder(args);
    AddCursorAppEnvAliases(builder.Configuration);

    var port = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrWhiteSpace(port))
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "DocIntelApi"));

    // ── SERVICES ─────────────────────────────────────────────────────────

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApi();

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    var connectionString = PostgresUrl.Resolve(builder.Configuration);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Log.Warning(
            "No Postgres connection string. Set ConnectionStrings__DefaultConnection or DATABASE_URL.");
        connectionString = "Host=127.0.0.1;Database=docintel;Username=docintel";
    }

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString)
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

    var qdrant = QdrantOptions.From(builder.Configuration);
    builder.Services.AddSingleton(_ =>
        new QdrantClient(
            host: qdrant.Host,
            port: qdrant.Port,
            https: qdrant.Https,
            apiKey: qdrant.ApiKey
        )
    );

    var jwtSecret = builder.Configuration["Jwt:Secret"];
    if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    {
        jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Log.Warning(
            "Jwt:Secret is missing or shorter than 32 characters. " +
            "Using an ephemeral secret for this process. Set Jwt__Secret for durable sessions.");
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["Gemini:ApiKey"]))
    {
        Log.Warning(
            "Gemini:ApiKey is not set. The UI will still boot; indexing and Ask will fail until Gemini__ApiKey is provided.");
    }

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
    builder.Services.AddHealthChecks();

    // ── BUILD ─────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ── MIDDLEWARE PIPELINE ───────────────────────────────────────────────
    app.UseForwardedHeaders();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (httpContext.Request.Path.StartsWithSegments("/health"))
                return Serilog.Events.LogEventLevel.Debug;

            if (ex is not null || httpContext.Response.StatusCode >= 500)
                return Serilog.Events.LogEventLevel.Error;
            if (httpContext.Response.StatusCode >= 400)
                return Serilog.Events.LogEventLevel.Warning;
            return Serilog.Events.LogEventLevel.Information;
        };
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
        };
    });

    // Global exception → ProblemDetails (before the rest of the pipeline)
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    var runningInContainer = string.Equals(
        Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase);
    if (!runningInContainer && string.IsNullOrWhiteSpace(port) && app.Environment.IsDevelopment())
        app.UseHttpsRedirection();

    // SPA UI (wwwroot) — same origin as API, no CORS needed
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── ENDPOINTS ─────────────────────────────────────────────────────────
    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapControllers();
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.MapFallbackToFile("index.html");

    await ApplyMigrationsAsync(app);

    if (app.Environment.IsDevelopment())
        await DevAdminSeeder.SeedAsync(app);

    Log.Information(
        "DocIntelApi starting in {Environment} on {Urls}",
        app.Environment.EnvironmentName,
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? Environment.GetEnvironmentVariable("PORT")
            ?? "(launch profile)");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "DocIntelApi terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static void AddCursorAppEnvAliases(ConfigurationManager config)
{
    // Cursor Apps secrets must be UPPER_CASE; ASP.NET Core keys use Jwt:Secret.
    var aliases = new Dictionary<string, string?>();

    void Alias(string dest, params string[] keys)
    {
        if (!string.IsNullOrWhiteSpace(config[dest]))
            return;

        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
                continue;
            aliases[dest] = value;
            return;
        }
    }

    Alias("Gemini:ApiKey", "GEMINI_API_KEY");
    Alias("Jwt:Secret", "JWT_SECRET");
    Alias("Qdrant:Host", "QDRANT_HOST");
    Alias("Qdrant:Port", "QDRANT_PORT");
    Alias("Qdrant:Https", "QDRANT_HTTPS");
    Alias("Qdrant:ApiKey", "QDRANT_API_KEY");

    if (aliases.Count > 0)
        config.AddInMemoryCollection(aliases);
}

static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseStartup");

    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex,
            "Could not apply database migrations. The UI will still serve; API calls that need Postgres will fail until the database is reachable.");
    }
}

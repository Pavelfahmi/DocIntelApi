using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace DocIntelApi.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 ProblemDetails responses
/// so controllers stay thin and clients always get a consistent error shape.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            _logger.LogError(exception, "Unhandled exception after response started");
            throw exception;
        }

        var (status, title) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);
        else
            _logger.LogWarning(exception, "Handled exception for {Method} {Path}: {Message}",
                context.Request.Method, context.Request.Path, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        if (_env.IsDevelopment() && status >= StatusCodes.Status500InternalServerError)
            problem.Extensions["trace"] = exception.ToString();

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static (int Status, string Title) Map(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            (StatusCodes.Status401Unauthorized, "Unauthorized"),

        InvalidOperationException ex when IsConflict(ex) =>
            (StatusCodes.Status409Conflict, "Conflict"),

        InvalidOperationException =>
            (StatusCodes.Status400BadRequest, "Bad request"),

        KeyNotFoundException =>
            (StatusCodes.Status404NotFound, "Not found"),

        HttpRequestException http =>
            MapHttp(http),

        OperationCanceledException =>
            (499, "Request cancelled"),

        _ =>
            (StatusCodes.Status500InternalServerError, "Internal server error")
    };

    private static (int Status, string Title) MapHttp(HttpRequestException http)
    {
        var status = http.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => StatusCodes.Status429TooManyRequests,
            HttpStatusCode.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status502BadGateway
        };

        var title = status switch
        {
            StatusCodes.Status429TooManyRequests => "AI rate limited",
            StatusCodes.Status503ServiceUnavailable => "AI temporarily unavailable",
            _ => "AI provider error"
        };

        return (status, title);
    }

    private static bool IsConflict(InvalidOperationException ex) =>
        ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
}

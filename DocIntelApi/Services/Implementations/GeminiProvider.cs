using DocIntelApi.Services.Interfaces;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace DocIntelApi.Services.Implementations;

public class GeminiProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiProvider> _logger;

    // Base URLs for Gemini APIs
    private const string EmbedBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string ChatBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    public GeminiProvider(
        HttpClient http,
        IConfiguration config,
        ILogger<GeminiProvider> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    // ── EMBED ────────────────────────────────────────────────────────
    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken ct = default,
        string? taskType = null)
    {
        var apiKey = _config["Gemini:ApiKey"]!;
        // text-embedding-004 was shut down Jan 2026 — use gemini-embedding-001
        var model = _config["Gemini:EmbeddingModel"] ?? "gemini-embedding-001";

        var url = $"{EmbedBaseUrl}/{model}:embedContent?key={apiKey}";

        var body = new
        {
            model = $"models/{model}",
            content = new
            {
                parts = new[] { new { text } }
            },
            taskType = taskType ?? "RETRIEVAL_DOCUMENT"
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogDebug(
            "Embedding text of length {Length} with model {Model}",
            text.Length, model);

        var response = await _http.PostAsync(url, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Gemini embed failed ({StatusCode}) model={Model}: {Body}",
                (int)response.StatusCode, model, responseJson);
            throw CreateGeminiException(response.StatusCode, "indexing", responseJson);
        }

        using var doc = JsonDocument.Parse(responseJson);
        var valuesArray = doc.RootElement
            .GetProperty("embedding")
            .GetProperty("values");

        var vector = valuesArray
            .EnumerateArray()
            .Select(v => v.GetSingle())
            .ToArray();

        _logger.LogDebug(
            "Got embedding vector of {Dimensions} dimensions", vector.Length);

        return vector;
    }

    // ── COMPLETE ─────────────────────────────────────────────────────
    public async Task<LlmCompletionResult> CompleteAsync(
        string prompt, CancellationToken ct = default)
    {
        var apiKey = _config["Gemini:ApiKey"]!;
        // gemini-2.0/2.5 Flash blocked for new API keys — use gemini-3.5-flash
        var model = _config["Gemini:ChatModel"] ?? "gemini-3.5-flash";

        var url = $"{ChatBaseUrl}/{model}:generateContent?key={apiKey}";

        var body = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = prompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 2048
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Gemini generateContent failed ({StatusCode}) model={Model}: {Body}",
                (int)response.StatusCode, model, responseJson);
            throw CreateGeminiException(response.StatusCode, "answering", responseJson);
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        var text = ExtractTextFromCandidates(root);
        var (promptTokens, outputTokens, totalTokens) = ExtractUsage(root);

        return new LlmCompletionResult(text, promptTokens, outputTokens, totalTokens);
    }

    // ── STREAM ───────────────────────────────────────────────────────
    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = _config["Gemini:ApiKey"]!;
        var model = _config["Gemini:ChatModel"] ?? "gemini-3.5-flash";

        var url = $"{ChatBaseUrl}/{model}:streamGenerateContent?key={apiKey}&alt=sse";

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 2048
            }
        };

        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };

        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Gemini streamGenerateContent failed ({StatusCode}) model={Model}: {Body}",
                (int)response.StatusCode, model, errorBody);
            throw CreateGeminiException(response.StatusCode, "answering", errorBody);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            using var tokenDoc = JsonDocument.Parse(data);
            var token = ExtractTextFromCandidates(tokenDoc.RootElement);

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    private static HttpRequestException CreateGeminiException(
        HttpStatusCode statusCode,
        string action,
        string responseBody)
    {
        var message = MapGeminiUserMessage(statusCode, action, responseBody);
        return new HttpRequestException(message, null, statusCode);
    }

    private static string MapGeminiUserMessage(
        HttpStatusCode statusCode,
        string action,
        string responseBody)
    {
        var body = responseBody ?? string.Empty;
        var busy = statusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
            || body.Contains("high demand", StringComparison.OrdinalIgnoreCase)
            || body.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
            || body.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

        if (busy)
        {
            return action == "indexing"
                ? "The AI service is busy right now. Please wait a moment and try uploading again."
                : "The AI service is busy right now. Please wait a moment and ask again.";
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return "The AI service rejected this request. Please check the API key configuration.";

        if ((int)statusCode >= 500)
            return "The AI service had a temporary problem. Please try again in a moment.";

        return "We couldn't reach the AI service. Please try again.";
    }

    /// <summary>
    /// Reads answer text from Gemini candidates. Skips thought-only parts used by
    /// thinking models (e.g. gemini-2.5-flash).
    /// </summary>
    private static string ExtractTextFromCandidates(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
            return string.Empty;

        if (!candidates[0].TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
            return string.Empty;

        string? lastText = null;
        foreach (var part in parts.EnumerateArray())
        {
            // Skip internal "thought" parts when present
            if (part.TryGetProperty("thought", out var thought)
                && thought.ValueKind is JsonValueKind.True)
                continue;

            if (part.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString();
                if (!string.IsNullOrEmpty(text))
                    lastText = text;
            }
        }

        return lastText ?? string.Empty;
    }

    private static (int Prompt, int Output, int Total) ExtractUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
            return (0, 0, 0);

        static int ReadInt(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetInt32()
                : 0;

        var prompt = ReadInt(usage, "promptTokenCount");
        var output = ReadInt(usage, "candidatesTokenCount");
        var thoughts = ReadInt(usage, "thoughtsTokenCount");
        var total = ReadInt(usage, "totalTokenCount");

        if (output == 0 && thoughts > 0)
            output = thoughts;
        else
            output += thoughts;

        if (total == 0)
            total = prompt + output;

        return (prompt, output, total);
    }
}
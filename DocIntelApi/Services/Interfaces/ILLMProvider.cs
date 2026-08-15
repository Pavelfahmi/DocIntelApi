namespace DocIntelApi.Services.Interfaces;

public record LlmCompletionResult(
    string Text,
    int PromptTokens,
    int OutputTokens,
    int TotalTokens);

// Provider-agnostic contract
// Swap Gemini → Groq → Ollama by changing ONE line in Program.cs
public interface ILLMProvider
{
    Task<float[]> EmbedAsync(
        string text,
        CancellationToken ct = default,
        string? taskType = null);

    Task<LlmCompletionResult> CompleteAsync(
        string prompt, CancellationToken ct = default);

    IAsyncEnumerable<string> StreamAsync(
        string prompt, CancellationToken ct = default);
}

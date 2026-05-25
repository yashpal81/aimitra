using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Aimitra.WebChat.Services;

/// <summary>
/// Calls the Google Gemini text-embedding-004 REST API to produce float32 embeddings.
/// Output dimensionality: 768 (default for text-embedding-004).
/// </summary>
public sealed class GeminiEmbeddingGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    /// <summary>Number of float32 dimensions returned by the configured model.</summary>
    public int Dimensions => 768;

    public GeminiEmbeddingGenerator(HttpClient httpClient, string apiKey, string model = "text-embedding-004")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Gemini API key is required.", nameof(apiKey));

        _httpClient = httpClient;
        _apiKey     = apiKey;
        _model      = model;
    }

    /// <summary>
    /// Returns a 768-dimensional embedding vector for <paramref name="text"/>.
    /// </summary>
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text is required.", nameof(text));

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:embedContent?key={_apiKey}";

        var requestBody = new
        {
            model   = $"models/{_model}",
            content = new { parts = new[] { new { text } } }
        };

        using var response = await _httpClient
            .PostAsJsonAsync(url, requestBody, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<GeminiEmbedResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result?.Embedding?.Values
               ?? throw new InvalidOperationException("Gemini returned an empty embedding.");
    }

    // ---------- JSON shape returned by Gemini embedContent endpoint ----------

    private sealed record GeminiEmbedResponse(
        [property: JsonPropertyName("embedding")] GeminiEmbedValues? Embedding);

    private sealed record GeminiEmbedValues(
        [property: JsonPropertyName("values")] float[]? Values);
}

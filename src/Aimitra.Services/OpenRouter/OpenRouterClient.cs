using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Services.Interfaces;

namespace Aimitra.Services.OpenRouter
{
    public sealed class OpenRouterClient : IOpenRouterClient
    {
        private const string DefaultEndpoint = "https://openrouter.ai/api/v1/chat/completions";
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public OpenRouterClient(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public async Task<string> GetChatCompletionAsync(string model, string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("Model name is required.", nameof(model));
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt text is required.", nameof(prompt));
            }

            using (var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var payload = new OpenRouterRequest
                {
                    Model = model,
                    Messages = new[]
                    {
                        new OpenRouterMessage { Role = "user", Content = prompt }
                    },
                    Temperature = 0.2m,
                    MaxTokens = 800
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload, options), Encoding.UTF8, "application/json");

                try
                {
                    using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException($"OpenRouter API error: {response.StatusCode}. Response: {content}");
                        }

                        var result = JsonSerializer.Deserialize<OpenRouterResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (result == null || result.Choices == null || result.Choices.Length == 0)
                        {
                            throw new InvalidOperationException($"OpenRouter API returned an invalid response: {content}");
                        }

                        return result.Choices[0].Message.Content ?? string.Empty;
                    }
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException($"OpenRouter request failed. Check network connectivity and DNS for '{DefaultEndpoint}'.", ex);
                }
            }
        }

        private sealed class OpenRouterRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public OpenRouterMessage[] Messages { get; set; }

            [JsonPropertyName("temperature")]
            public decimal Temperature { get; set; }

            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }
        }

        private sealed class OpenRouterMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
        }

        private sealed class OpenRouterResponse
        {
            public OpenRouterChoice[] Choices { get; set; }
        }

        private sealed class OpenRouterChoice
        {
            public OpenRouterMessage Message { get; set; }
        }
    }
}

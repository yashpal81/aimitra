using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace METASYNAPSE.WebChat.Services
{
    public class WebchatClient
    {
        private readonly HttpClient _http;
        private readonly string _apiUrl;
        private readonly string _apiSecret;

        public WebchatClient(HttpClient http)
        {
            _http = http;
            _apiUrl = Environment.GetEnvironmentVariable("WEBCHAT_API_URL") ?? string.Empty;
            _apiSecret = Environment.GetEnvironmentVariable("WEBCHAT_API_SECRET") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_apiUrl) && !_apiUrl.EndsWith('/')) _apiUrl += '/';
        }

        // Sends a JSON payload to the webchat API with an HMAC-SHA256 signature header
        public async Task<HttpResponseMessage> SendSignedPayloadAsync(string relativePath, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            // compute signature
            var signature = ComputeHmacSha256Hex(bytes, _apiSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_apiUrl), relativePath));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("X-Signature", $"sha256={signature}");

            return await _http.SendAsync(request).ConfigureAwait(false);
        }

        private static string ComputeHmacSha256Hex(byte[] message, string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret ?? string.Empty);
            using var hmac = new HMACSHA256(key);
            var sig = hmac.ComputeHash(message);
            var sb = new StringBuilder(sig.Length * 2);
            foreach (var b in sig) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}

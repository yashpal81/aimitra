using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace METASYNAPSE.WebChat.Services
{
    public class CanvasAuthService
    {
        private readonly string _consumerSecret;

        public CanvasAuthService()
        {
            _consumerSecret = Environment.GetEnvironmentVariable("CANVAS_CONSUMER_SECRET") ?? string.Empty;
        }

        // Returns parsed payload JsonElement on success, or null on failure
        public JsonElement? VerifySignedRequest(string signedRequest)
        {
            try
            {
                var parts = signedRequest.Split('.');
                if (parts.Length != 2) return null;

                var sig = parts[0];
                var payloadB64 = parts[1];

                var payloadBytes = Base64UrlDecode(payloadB64);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);

                // compute HMAC-SHA256 over payloadB64 using consumer secret
                var expected = ComputeHmacSha256Base64Url(payloadB64, _consumerSecret);
                if (!SecureEquals(sig, expected)) return null;

                using var doc = JsonDocument.Parse(payloadJson);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        private static string ComputeHmacSha256Base64Url(string data, string secret)
        {
            var key = Encoding.UTF8.GetBytes(secret ?? string.Empty);
            var msg = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA256(key);
            var sig = hmac.ComputeHash(msg);
            return Base64UrlEncode(sig);
        }

        private static bool SecureEquals(string a, string b)
        {
            if (a is null || b is null) return false;
            var aa = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (aa.Length != bb.Length) return false;
            var diff = 0;
            for (var i = 0; i < aa.Length; i++) diff |= aa[i] ^ bb[i];
            return diff == 0;
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private static string Base64UrlEncode(byte[] input)
        {
            var s = Convert.ToBase64String(input);
            s = s.TrimEnd('=');
            s = s.Replace('+', '-').Replace('/', '_');
            return s;
        }
    }
}

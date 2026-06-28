using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace METASYNAPSE.WebChat.Controllers
{
    public class CanvasController : Controller
    {
        private readonly string _consumerSecret;

        public CanvasController()
        {
            _consumerSecret = Environment.GetEnvironmentVariable("CANVAS_CONSUMER_SECRET") ?? string.Empty;
           // _consumerSecret ="DA226023079E1A03EB4B73489F744D692B845B0D490E574906B982E884C7A0DE";
        }

        [HttpGet("canvas/load")]
        public IActionResult Load()
        {
            ViewBag.UserName = "Salesforce User";
            ViewBag.SalesforceContext = "{}";
            return View("Index");
        }

        [HttpPost("canvas/load")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Load([FromForm] string signed_request)
        {
            Console.WriteLine("Received Canvas load request with signed_request:");
            Console.WriteLine(signed_request);
            if (string.IsNullOrEmpty(signed_request))
            {
                return BadRequest("Missing signed_request parameter.");
            }

            if (string.IsNullOrWhiteSpace(_consumerSecret))
            {
                return StatusCode(500, "Canvas consumer secret is not configured.");
            }

            try
            {
                var parts = signed_request.Split('.');
                if (parts.Length != 2)
                {
                    return BadRequest("Invalid signed_request format.");
                }

                var encodedSignature = parts[0];
                var encodedPayload = parts[1];

                if (!VerifySignature(encodedPayload, encodedSignature, _consumerSecret))
                {
                    return Forbid("Invalid signature. Request untrusted.");
                }

                var payloadBytes = Base64UrlDecode(encodedPayload);
                var jsonString = Encoding.UTF8.GetString(payloadBytes);
                using var jsonDoc = JsonDocument.Parse(jsonString);

                string? userName = null;
                if (jsonDoc.RootElement.TryGetProperty("context", out var contextElem) && contextElem.ValueKind == JsonValueKind.Object &&
                    contextElem.TryGetProperty("user", out var userElem) && userElem.ValueKind == JsonValueKind.Object &&
                    userElem.TryGetProperty("fullName", out var fullNameElem))
                {
                    userName = fullNameElem.GetString();
                }

                ViewBag.UserName = userName ?? "Salesforce User";
                ViewBag.SalesforceContext = jsonString;

// Extract user details from the decoded Salesforce JSON payload
    var userId = jsonDoc.RootElement.GetProperty("context").GetProperty("user").GetProperty("userId").GetString();
    var userEmail = jsonDoc.RootElement.GetProperty("context").GetProperty("user").GetProperty("email").GetString();
    var sfAccessToken = jsonDoc.RootElement.GetProperty("client").GetProperty("oauthToken").GetString();

    // Create identity claims based on Salesforce's authenticated data
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, userId),
        new Claim(ClaimTypes.Email, userEmail),
        new Claim("SalesforceAccessToken", sfAccessToken) // Save this if Semantic Kernel needs to call back to SF API
    };

    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

    // Issue the secure cookie to the browser
    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));
 Console.WriteLine("    Successfully authenticated Canvas user and set cookie. UserId: {userId}, Email: {userEmail}");
                return View("Index");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error decoding Canvas request: {ex.Message}");
            }
        }

      private static bool VerifySignature(string payload, string signature, string secret)
{
    // Compute the standard base64 version to match Salesforce's signature output
    var expectedSignature = ComputeHmacSha256StandardBase64(payload, secret);
    return SecureEquals(expectedSignature, signature);
}

        private static string ComputeHmacSha256StandardBase64(string payload, string secret)
{
    var keyBytes = Encoding.UTF8.GetBytes(secret);
    var payloadBytes = Encoding.UTF8.GetBytes(payload);
    using var hmac = new HMACSHA256(keyBytes);
    var hash = hmac.ComputeHash(payloadBytes);
    
    // Salesforce signature is standard Base64, NOT Base64Url
    return Convert.ToBase64String(hash); 
}

        private static bool SecureEquals(string a, string b)
        {
            if (a is null || b is null || a.Length != b.Length)
                return false;

            var result = 0;
            for (var i = 0; i < a.Length; i++)
            {
                result |= a[i] ^ b[i];
            }
            return result == 0;
        }

        private static byte[] Base64UrlDecode(string base64)
        {
            var s = base64.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 0: break;
                default: s += new string('=', 4 - s.Length % 4); break;
            }
            return Convert.FromBase64String(s);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}

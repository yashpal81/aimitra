using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using METASYNAPSE.WebChat.Hubs;
using METASYNAPSE.WebChat.Configuration;
using METASYNAPSE.Services.Orchestration;
using METASYNAPSE.Services.Plugins;
using METASYNAPSE.SamplePlugins.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.OpenAI;
using METASYNAPSE.WebChat.Services;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

var environmentName = "local"; // "Environment.GetEnvironmentVariable("METASYNAPSE_ENVIRONMENT")?.Trim();"
EnvFileLoader.Load(environmentName);

// Read runtime configuration from environment (same vars used by console app)
var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? string.Empty;
var openAIUrl = Environment.GetEnvironmentVariable("OPENAI_URL") ?? string.Empty;
var openAIModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? string.Empty;
var presidioEndpoint = Environment.GetEnvironmentVariable("PRESIDIO_ENDPOINT") ?? string.Empty;
var routeAgent = Environment.GetEnvironmentVariable("ROUTE_AGENT") ?? "topic_selector";
var geminiApiKey = Environment.GetEnvironmentVariable("API_KEY") ?? string.Empty;
var documentMemoryProvider = Environment.GetEnvironmentVariable("DOCUMENT_MEMORY_PROVIDER")?.Trim().ToLowerInvariant() ?? "custom";
var chatbotFrameAncestors = Environment.GetEnvironmentVariable("CHATBOT_FRAME_ANCESTORS")?.Trim();
var salesforceOrigins = Environment.GetEnvironmentVariable("SALESFORCE_ORIGINS")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? new[] { "https://*.lightning.force.com", "https://*.my.salesforce.com", "https://*.visual.force.com", "https://undefined-deceiver-runny.ngrok-free.dev" };
if (string.IsNullOrWhiteSpace(chatbotFrameAncestors))
{
    chatbotFrameAncestors = "'self' https://*.lightning.force.com https://*.my.salesforce.com https://*.visual.force.com https://undefined-deceiver-runny.ngrok-free.dev";
}

builder.WebHost.UseUrls("http://0.0.0.0:5000");
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentDefinitionStore>();
builder.Services.AddSingleton<AgentDefinitionLoader>();
builder.Services.AddSingleton<ConnectionDiagnosticsService>();
builder.Services.AddSingleton<ExternalSessionService>();
// Canvas / Salesforce integration
builder.Services.AddSingleton<METASYNAPSE.WebChat.Services.CanvasAuthService>();
builder.Services.AddHttpClient<METASYNAPSE.WebChat.Services.WebchatClient>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "SF_Canvas_Session";
        options.Cookie.SameSite = SameSiteMode.None; // Required because your app is in an iframe
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Required for Salesforce cross-domain
        options.ExpireTimeSpan = TimeSpan.FromHours(2); // Match Salesforce session timeout
    });

// ── Document memory: Gemini embeddings + SQLite vector store ─────────────────
var knowledgeDbPath = Path.Combine(AppContext.BaseDirectory, "App_Data", "knowledge-base", "vectors.db");
Directory.CreateDirectory(Path.GetDirectoryName(knowledgeDbPath)!);

// Previous sessions DB (simple SQLite store)
var sessionsDbPath = Path.Combine(AppContext.BaseDirectory, "App_Data", "sessions.db");
Directory.CreateDirectory(Path.GetDirectoryName(sessionsDbPath)!);
builder.Services.AddSingleton<IPreviousSessionService>(sp => new SqlitePreviousSessionService(sessionsDbPath));

if (documentMemoryProvider == "kernelmemory")
{
    // Kernel Memory 0.35 currently conflicts with the Azure.AI.OpenAI version
    // resolved by the rest of this solution (via Semantic Kernel). Fall back to the
    // custom vector-store implementation so the app can start reliably.
    builder.Services.AddHttpClient<GeminiEmbeddingGenerator>();
    builder.Services.AddSingleton(sp =>
        new GeminiEmbeddingGenerator(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiEmbeddingGenerator)),
            geminiApiKey));
    builder.Services.AddSingleton(new SqliteVecDocumentStore(knowledgeDbPath));
    builder.Services.AddSingleton<IDocumentMemoryService, DocumentVectorStoreService>();
}
else
{
    builder.Services.AddHttpClient<GeminiEmbeddingGenerator>();
    builder.Services.AddSingleton(sp =>
        new GeminiEmbeddingGenerator(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiEmbeddingGenerator)),
            geminiApiKey));
    builder.Services.AddSingleton(new SqliteVecDocumentStore(knowledgeDbPath));
    if (string.IsNullOrWhiteSpace(geminiApiKey))
    {
        builder.Services.AddSingleton<IDocumentMemoryService, DocumentMemoryService>();
    }
    else
    {
        builder.Services.AddSingleton<IDocumentMemoryService, DocumentVectorStoreService>();
    }
}
// ─────────────────────────────────────────────────────────────────────────────


// Register SemanticKernelOrchestrator and TopicOrchestrator as singletons
builder.Services.AddSingleton(sp =>
{
    var loader = sp.GetRequiredService<AgentDefinitionLoader>();
    var topics = loader.LoadActiveTopics();

    var kernelOrchestrator = new SemanticKernelOrchestrator(routeAgent, apiKey, openAIModel, openAIUrl, presidioEndpoint, topics);
    return kernelOrchestrator;
});

builder.Services.AddSingleton(sp =>
{
    var kernel = sp.GetRequiredService<SemanticKernelOrchestrator>();
    var orchestrator = new TopicOrchestrator(kernel);
    return orchestrator;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSalesforceOrigins", policy =>
    {
        policy.SetIsOriginAllowed(origin => salesforceOrigins.Any(pattern => OriginPatternMatches(pattern, origin)))
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Essential if passing session context parameters via headers/cookies
    });
});

static bool OriginPatternMatches(string pattern, string origin)
{
    if (pattern.Contains('*'))
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(origin, regex, RegexOptions.IgnoreCase);
    }

    return string.Equals(origin, pattern, StringComparison.OrdinalIgnoreCase);
}

var app = builder.Build();
app.UseCors("AllowSalesforceOrigins");
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// In Program.cs of your Razor AI App
app.Use(async (context, next) =>
{
    // Allow this specific endpoint to be framed by your second app
    if (context.Request.Path.StartsWithSegments("/chatbot"))
    {// Clear the default deny headers
        context.Response.Headers.Remove("X-Frame-Options");
        
        // Allow it to be framed locally for testing
        context.Response.Headers.Append("Content-Security-Policy", $"frame-ancestors {chatbotFrameAncestors};");
        // If the embedded chatbot is opened with external session info (from LWC),
        // capture and register it with the ExternalSessionService so server-side
        // kernels and callables can resolve the mapping later.
        // Supported query params: sessionId, externalSessionId, collection
        var qs = context.Request.Query;
        var extSessionId = qs["sessionId"].ToString();
        if (string.IsNullOrWhiteSpace(extSessionId)) extSessionId = qs["externalSessionId"].ToString();
        var collection = qs["collection"].ToString();
        if (!string.IsNullOrWhiteSpace(extSessionId))
        {
            var sessions = context.RequestServices.GetRequiredService<ExternalSessionService>();
            sessions.Register(extSessionId, string.IsNullOrWhiteSpace(collection) ? null : collection);
            // Optionally expose a short response header confirming registration
            context.Response.Headers.Append("X-External-Session-Registered", "true");
        }
    }
    await next();
});




app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/google-drive/callback", async (HttpContext context) =>
{
    var code = context.Request.Query["code"].ToString();
    var error = context.Request.Query["error"].ToString();

    if (!string.IsNullOrWhiteSpace(error))
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync($"<h1>Google Drive OAuth error</h1><p>{System.Net.WebUtility.HtmlEncode(error)}</p>");
        return;
    }

    if (string.IsNullOrWhiteSpace(code))
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync("<h1>Google Drive OAuth callback</h1><p>No authorization code was returned.</p>");
        return;
    }

    try
    {
        var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/google-drive/callback";
        var plugin = new METASYNAPSE.SamplePlugins.Plugins.GoogleDrivePlugin();
        var result = await plugin.CompleteGoogleDriveOAuth(code, redirectUri).ConfigureAwait(false);

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync($"<h1>Google Drive OAuth completed</h1><p>{System.Net.WebUtility.HtmlEncode(result)}</p>");
    }
    catch (Exception ex)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync($"<h1>Google Drive OAuth failed</h1><pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre>");
    }
});

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapRazorPages();
    endpoints.MapBlazorHub();
    endpoints.MapHub<ChatHub>("/chathub");
    endpoints.MapFallbackToPage("/_Host");
    endpoints.MapGet("/diagnostics/signalr/connections", (ConnectionDiagnosticsService diag) =>
    {
        var connections = diag.GetAllConnections();
        var counts = diag.GetCountsByCollection();
        return Results.Json(new { connections, counts });
    });
});

// Simple test endpoint to forward JSON to configured webchat API with HMAC signing
app.MapPost("/webchat/send", async (HttpContext context, METASYNAPSE.WebChat.Services.WebchatClient client) =>
{
    using var doc = await JsonDocument.ParseAsync(context.Request.Body).ConfigureAwait(false);
    var result = await client.SendSignedPayloadAsync("messages", doc.RootElement).ConfigureAwait(false);
    return Results.StatusCode((int)result.StatusCode);
});

// Salesforce Canvas signed_request receiver
app.MapPost("/canvas", async (HttpContext context, METASYNAPSE.WebChat.Services.CanvasAuthService canvasAuth, ExternalSessionService sessions) =>
{
    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest("Expected form POST with signed_request");
    }

    var form = await context.Request.ReadFormAsync();
    var signedReq = form["signed_request"].ToString();
    if (string.IsNullOrWhiteSpace(signedReq))
    {
        return Results.BadRequest("missing signed_request");
    }

    var payload = canvasAuth.VerifySignedRequest(signedReq);
    if (!payload.HasValue)
    {
        return Results.Unauthorized();
    }

    var jsonPayload = payload.Value;
    string? extSessionId = null;
    if (jsonPayload.TryGetProperty("client", out var clientProp) && clientProp.ValueKind == System.Text.Json.JsonValueKind.Object)
    {
        if (clientProp.TryGetProperty("userId", out var u1)) extSessionId = u1.GetString();
        if (string.IsNullOrWhiteSpace(extSessionId) && clientProp.TryGetProperty("user_id", out var u2)) extSessionId = u2.GetString();
    }
    if (string.IsNullOrWhiteSpace(extSessionId) && jsonPayload.TryGetProperty("user_id", out var u3)) extSessionId = u3.GetString();
    if (string.IsNullOrWhiteSpace(extSessionId) && jsonPayload.TryGetProperty("user", out var u4) && u4.ValueKind == System.Text.Json.JsonValueKind.Object)
    {
        if (u4.TryGetProperty("id", out var u5)) extSessionId = u5.GetString();
    }

    if (string.IsNullOrWhiteSpace(extSessionId))
    {
        if (jsonPayload.TryGetProperty("issued_at", out var issued) && issued.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            extSessionId = issued.GetInt64().ToString();
        }
        else
        {
            extSessionId = Guid.NewGuid().ToString("N");
        }
    }

    sessions.Register(extSessionId, null);

    // Redirect to the embedded chatbot page and include externalSessionId so the existing middleware picks it up
    var redirectUrl = $"/chatbot?externalSessionId={System.Net.WebUtility.UrlEncode(extSessionId)}";
    context.Response.Redirect(redirectUrl);
    return Results.Redirect(redirectUrl);
});

// External session registration is handled when the embedded chatbot is opened
// with query parameters. Example: /chatbot?sessionId=abc123&collection=user_xyz




app.Run();


// can be deleted if code doesnt work
static Uri NormalizeOpenAiCompatibleEndpoint(string endpoint)
{
    var trimmed = endpoint.Trim().TrimEnd('/');
    var uri = new Uri(trimmed, UriKind.Absolute);

    if (uri.Host.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
    {
        if (uri.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.AbsolutePath, "/v1", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.AbsolutePath) ||
            uri.AbsolutePath == "/")
        {
            return new Uri($"{uri.Scheme}://{uri.Host}/api/v1");
        }
    }

    if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        trimmed = trimmed[..^"/chat/completions".Length];

    return new Uri(trimmed, UriKind.Absolute);
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Aimitra.WebChat.Hubs;
using Aimitra.WebChat.Configuration;
using Aimitra.Services.Orchestration;
using Aimitra.Services.Plugins;
using Aimitra.SamplePlugins.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.OpenAI;
using Aimitra.WebChat.Services;

var builder = WebApplication.CreateBuilder(args);

var environmentName = "local"; // "Environment.GetEnvironmentVariable("AIMITRA_ENVIRONMENT")?.Trim();"
EnvFileLoader.Load(environmentName);

// Read runtime configuration from environment (same vars used by console app)
var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? string.Empty;
var openAIUrl = Environment.GetEnvironmentVariable("OPENAI_URL") ?? string.Empty;
var openAIModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? string.Empty;
var presidioEndpoint = Environment.GetEnvironmentVariable("PRESIDIO_ENDPOINT") ?? string.Empty;
var routeAgent = Environment.GetEnvironmentVariable("ROUTE_AGENT") ?? "topic_selector";
var geminiApiKey = Environment.GetEnvironmentVariable("API_KEY") ?? string.Empty;
var documentMemoryProvider = Environment.GetEnvironmentVariable("DOCUMENT_MEMORY_PROVIDER")?.Trim().ToLowerInvariant() ?? "custom";

builder.WebHost.UseUrls("http://0.0.0.0:5000");
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentDefinitionStore>();
builder.Services.AddSingleton<AgentDefinitionLoader>();

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

var app = builder.Build();

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
        context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors 'self' https://your-second-webapp.com;");
    }
    await next();
});

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapHub<ChatHub>("/chathub");
app.MapFallbackToPage("/_Host");




app.Run();

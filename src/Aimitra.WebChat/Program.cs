using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Aimitra.WebChat.Hubs;
using Aimitra.WebChat.Configuration;
using Aimitra.Services.Orchestration;
using Aimitra.Services.Plugins;
using Aimitra.SamplePlugins.Plugins;
using Microsoft.SemanticKernel;
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

builder.WebHost.UseUrls("http://0.0.0.0:5000");
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentDefinitionStore>();
builder.Services.AddSingleton<AgentDefinitionLoader>();
builder.Services.AddSingleton<IDocumentMemoryService, DocumentMemoryService>();


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

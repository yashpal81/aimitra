using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Aimitra.WebChat.Hubs;
using Aimitra.WebChat.Configuration;
using Aimitra.Services.Orchestration;
using Aimitra.Services.Plugins;
using Aimitra.SamplePlugins.Plugins;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

var environmentName = "local"; // "Environment.GetEnvironmentVariable("AIMITRA_ENVIRONMENT")?.Trim();"
EnvFileLoader.Load(environmentName);

// Read runtime configuration from environment (same vars used by console app)
var apiKey = Environment.GetEnvironmentVariable("API_KEY") ?? string.Empty;
var openAIUrl = Environment.GetEnvironmentVariable("OPENAI_URL") ?? string.Empty;
var openAIModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? string.Empty;
var presidioEndpoint = Environment.GetEnvironmentVariable("PRESIDIO_ENDPOINT") ?? string.Empty;
var routeAgent = Environment.GetEnvironmentVariable("ROUTE_AGENT") ?? "topic_selector";

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();

// Register SemanticKernelOrchestrator and TopicOrchestrator as singletons
builder.Services.AddSingleton(sp =>
{
    // Build the topics similar to Console app
    var topics = new Topic[]
    {
        new Topic(
            Name: "DatabaseTools",
            Description: "Answer questions about databases, generate SQL queries, retrieve schema information.",
            Actions: new[] { KernelPluginFactory.CreateFromObject(new DatabasePlugin(), "DatabaseTools") }),

        new Topic(
            Name: "GreetingPlugin",
            Description: "Send a friendly greeting or welcome message to a user by name.",
            Actions: new[] { KernelPluginFactory.CreateFromObject(new SampleGreetingPlugin(), "GreetingTools") })//,

        // new Topic(
        //     Name: "AstrologerPlugin",
        //     Description: "Provide astrological readings or horoscopes for a person.",
        //     Actions: new[] { KernelPluginFactory.CreateFromObject(new Aimitra.Services.Plugins.AstrologerPlugin(), "AstrologyTools") })
    };

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

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapHub<ChatHub>("/chathub");
app.MapFallbackToPage("/_Host");

app.Run();

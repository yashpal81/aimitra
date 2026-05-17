using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;
using Aimitra.Services.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.ChatCompletion;
using Aimitra.Core.Interfaces;
using Aimitra.Services.Metadata;
using System.ClientModel;
using System.Text.RegularExpressions;
using Aimitra.Services.Plugins;
using Aimitra.Security;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace Aimitra.Services.Orchestration
{
    public class XLamStep
{
    public int step { get; set; }
    public string content { get; set; }
}

// Helper class for JSON parsing
public class ActionCall
{
    public string PluginName { get; set; }
    public string FunctionName { get; set; }
    public Dictionary<string, object> Arguments { get; set; }
    
    public Dictionary<string, object> Parameters { get; set; }
}

    public sealed class SemanticKernelOrchestrator
    {
        private const int MaxIterations = 1;
        private readonly string _model;
        private readonly string _apiKey;
        private readonly Uri _endpoint;
        private readonly string _presidioEndpoint;
        private readonly KernelPluginLoader _pluginLoader;
        private readonly string _routeAgent;
        public SemanticKernelOrchestrator(string routeAgent, string apiKey, string model, string endpoint, string presidioEndpoint)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("API key cannot be empty.", nameof(apiKey)) : apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("Model cannot be empty.", nameof(model)) : model;
            _endpoint = new Uri(endpoint);
            _presidioEndpoint = string.IsNullOrWhiteSpace(presidioEndpoint) ? throw new ArgumentException("Presidio endpoint cannot be empty.", nameof(presidioEndpoint)) : presidioEndpoint;
            _pluginLoader = new KernelPluginLoader(KernelPluginOptions.FromEnvironment());
            _routeAgent = string.IsNullOrWhiteSpace(routeAgent) ? throw new ArgumentException("Route agent cannot be empty.", nameof(routeAgent)) : routeAgent;
        }

        public async Task<ReasoningResult> GenerateSqlFromQuestionAsync(string question, DatabaseSchema schema, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(question))
                throw new ArgumentException("Question is required.", nameof(question));
            if (schema == null)
                throw new ArgumentNullException(nameof(schema));

            var history = new List<string>();
            string finalSql = null;
            string rawResponse = string.Empty;
            Console.WriteLine(question);
            Console.WriteLine(_model);
            Console.WriteLine(_endpoint);
            Console.WriteLine(_presidioEndpoint);
            Console.WriteLine("API key loaded from configuration.");
            // Build Semantic Kernel kernel with OpenRouter as OpenAI-compatible endpoint
            var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();
           // builder.promptexecutionsettings.MaxTokens = 2048;
            var settings = new OpenAIPromptExecutionSettings
                    {
                    MaxTokens = 1000,
                    Temperature = 0.7,
                    ModelId = "gpt-4",
                    //FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                };
            string provider = "OpenAI";//"openrouter";

            // Instantiate the engine
            var maskingEngine = new PiiMaskingEngine(_presidioEndpoint);

            // Register as both the INBOUND and OUTBOUND filter interceptor
            builder.Services.AddSingleton<IFunctionInvocationFilter>(maskingEngine);
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(maskingEngine);
            var kernel =builder.AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty,provider , null).Build();
            Console.WriteLine("Kernel built with OpenAI Chat Completion service.");
            Console.WriteLine(_routeAgent);
            switch(_routeAgent)
            {
                case "DatabaseTools":
                
                    Console.WriteLine("Registering DatabaseTools    ...");
                    kernel.Plugins.AddFromType<DatabasePlugin>("DatabaseTools");
                    break;
                case "AstrologerPlugin":
                    Console.WriteLine("Registering AstrologerPlugin...");
                    //kernel.Plugins.AddFromType<AstrologerPlugin>("AstrologyTools");
                    _pluginLoader.RegisterConfiguredPlugins(kernel);
                    break;
                case "GreetingPlugin":
                    Console.WriteLine("Registering GreetingPlugin...");
                    //kernel.Plugins.AddFromType<GreetingPlugin>("GreetingTools");
                    _pluginLoader.RegisterConfiguredPlugins(kernel);
                    break;    
                default:
                    _pluginLoader.RegisterConfiguredPlugins(kernel);
                    Console.WriteLine($"Unknown route agent specified: {_routeAgent}. No plugins will be registered.");
                    return new ReasoningResult(string.Empty, string.Empty, $"Unknown route agent specified: {_routeAgent}. No plugins will be registered.", history);
                    break;
           }

            kernel.Plugins.AddFromType<DatabasePlugin>("DatabaseTools");
            
            var chat = kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();


            Console.WriteLine("starting iterations");
            string finalResult ="";
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                Console.WriteLine($"Iteration {iteration + 1}/{MaxIterations}");
                var prompt = DatabaseQueryTool.BuildSemanticPrompt(question, schema, history);
                var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                chatHistory.AddUserMessage(prompt);
                var result = await chat.GetChatMessageContentAsync(chatHistory,executionSettings: settings, kernel: kernel, cancellationToken: cancellationToken).ConfigureAwait(false);
                rawResponse = result?.Content ?? string.Empty;
               // var plan = ParseActionPlan(rawResponse);
               Console.WriteLine("Parsing steps response..."+rawResponse);
      
            }

 
            return new ReasoningResult(finalResult ?? string.Empty, finalSql ?? string.Empty, rawResponse, history);
        }

    }
}


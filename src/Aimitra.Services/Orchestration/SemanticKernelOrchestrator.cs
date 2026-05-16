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

       public SemanticKernelOrchestrator(string apiKey, string model , string endpoint)
       
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("API key cannot be empty.", nameof(apiKey)) : apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("Model cannot be empty.", nameof(model)) : model;
            _endpoint = new Uri(endpoint);
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


            var kernel =builder.AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty,provider , null).Build();
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


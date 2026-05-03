using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;
using Aimitra.Services.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI; // Essential for AddOpenAIChatCompletion
using Microsoft.SemanticKernel.ChatCompletion;
using Aimitra.Core.Interfaces;
using Aimitra.Services.Metadata;
using System.ClientModel;

namespace Aimitra.Services.Orchestration
{
    public sealed class SemanticKernelOrchestrator
    {

        private const int MaxIterations = 5;
        private readonly string _model;
        private readonly string _apiKey;
        private readonly Uri _endpoint;

        public SemanticKernelOrchestrator(string apiKey, string model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free", string endpoint = "https://openrouter.ai/v1/chat/completions")
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
            Console.WriteLine(_apiKey);
            // Build Semantic Kernel kernel with OpenRouter as OpenAI-compatible endpoint
            var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();
            // builder.AddOpenAIChatCompletion(
            //     modelId: _model,
            //     endpoint: _endpoint,
            //     apiKey: _apiKey,
            //     orgId: null,
            //     serviceId: "openrouter",
            //     httpClient: null);
            var kernel =builder.AddOpenAIChatCompletion("nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free", new Uri("https://openrouter.ai/api/v1"), _apiKey, string.Empty, "openrouter", null)
             .AddOpenAIChatCompletion("google/gemma-4-26b-a4b-it:free", new Uri("https://openrouter.ai/api/v1"), _apiKey, string.Empty, "openrouter", null) .Build();
            //var kernel = builder.Build();
            var chat = kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();


            Console.WriteLine("starting iterations");
            string finalResult ="";
            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                Console.WriteLine($"Iteration {iteration + 1}/{MaxIterations}");
                var prompt = DatabaseQueryTool.BuildSemanticPrompt(question, schema, history);
                var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                chatHistory.AddUserMessage(prompt);
                var result = await chat.GetChatMessageContentAsync(chatHistory, kernel: kernel, cancellationToken: cancellationToken).ConfigureAwait(false);
                rawResponse = result?.Content ?? string.Empty;
                var plan = ParseActionPlan(rawResponse);

                history.Add($"Thought: {plan.Thought}");
                history.Add($"Action: {plan.Action}");
                history.Add($"ActionInput: {plan.ActionInput}");

                if (string.Equals(plan.Action, "DB_SCHEMA", StringComparison.OrdinalIgnoreCase))
                {
                    var observation = DatabaseQueryTool.BuildSchemaContext(schema);
                    history.Add("Observation: schema details returned");
                    history.Add(observation);
                    continue;
                }

                if (string.Equals(plan.Action, "WRITE_SQL", StringComparison.OrdinalIgnoreCase))
                {
                    finalSql = CleanSql(plan.ActionInput);
                    history.Add($"Observation: '{finalSql}'");
                    continue;
                    //break;
                }


                if (string.Equals(plan.Action, "Execute_SQL", StringComparison.OrdinalIgnoreCase))
                {
                    finalSql = CleanSql(plan.ActionInput);
                    finalResult = await executeSql(finalSql);
                    history.Add($"Observation: '{finalResult}'");
                    continue;
                    //break;
                }

                if (string.Equals(plan.Action, "FINISH", StringComparison.OrdinalIgnoreCase))
                {
                    finalSql = CleanSql(plan.ActionInput);
                    break;
                }

                history.Add($"Observation: Unknown action '{plan.Action}'.");
            }

            if (string.IsNullOrWhiteSpace(finalSql) && !string.IsNullOrWhiteSpace(rawResponse))
            {
                finalSql = CleanSql(rawResponse);
                history.Add("Observation: Parsed raw response as SQL fallback.");
            }

            return new ReasoningResult(finalResult ?? string.Empty, finalSql ?? string.Empty, rawResponse, history);
        }

        private static ActionPlan ParseActionPlan(string rawResponse)
        {
            if (TryExtractJson(rawResponse, out var json))
            {
                try
                {
                    using (var document = JsonDocument.Parse(json))
                    {
                        var root = document.RootElement;
                        var thought = GetStringProperty(root, "thought");
                        var action = GetStringProperty(root, "action");
                        var actionInput = GetStringProperty(root, "action_input") ?? GetStringProperty(root, "actionInput");

                        return new ActionPlan(
                            string.IsNullOrWhiteSpace(thought) ? string.Empty : thought,
                            string.IsNullOrWhiteSpace(action) ? "WRITE_SQL" : action,
                            string.IsNullOrWhiteSpace(actionInput) ? string.Empty : actionInput);
                    }
                }
                catch
                {
                    // fall through to raw fallback
                }
            }

            return new ActionPlan(string.Empty, "WRITE_SQL", rawResponse.Trim());
        }

        private static string GetStringProperty(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            return null;
        }

        private static bool TryExtractJson(string rawResponse, out string json)
        {
            json = null;
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return false;
            }

            var start = rawResponse.IndexOf('{');
            var end = rawResponse.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
            {
                return false;
            }

            json = rawResponse.Substring(start, end - start + 1);
            return true;
        }

        private static string CleanSql(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var trimmed = raw.Trim();
            return trimmed.Trim('`', '\'', '"').Trim();
        }
        private static async Task<string> executeSql(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }
            var provider = Environment.GetEnvironmentVariable("DB_PROVIDER")?.Trim().ToLowerInvariant();
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            IDbMetadataService metadataService = provider switch
            {
                "sqlserver" => new SqlServerMetadataService(),
                "postgres" => new PostgresMetadataService(),
                _ => null
            };

        string result  = await metadataService.ExecuteQueryAsJsonAsync(connectionString, raw).ConfigureAwait(false);
        return result;      
        }
        private sealed class ActionPlan
        {
            public ActionPlan(string thought, string action, string actionInput)
            {
                Thought = thought;
                Action = action;
                ActionInput = actionInput;
            }

            public string Thought { get; }

            public string Action { get; }

            public string ActionInput { get; }
        }
    }
}


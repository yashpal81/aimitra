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

        private const int MaxIterations = 2;
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
            Console.WriteLine("API key loaded from configuration.");
            // Build Semantic Kernel kernel with OpenRouter as OpenAI-compatible endpoint
            var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();
           // builder.promptexecutionsettings.MaxTokens = 2048;
            var settings = new OpenAIPromptExecutionSettings
                    {
                    MaxTokens = 1000,
                    Temperature = 0.7,
                    ModelId = "gpt-4"
                };
            // builder.AddOpenAIChatCompletion(
            //     modelId: _model,
            //     endpoint: _endpoint,
            //     apiKey: _apiKey,
            //     orgId: null,
            //     serviceId: "openrouter",
            //     httpClient: null);
          
            // string urlInference ="https://router.huggingface.co/v1";// "https://openrouter.ai/api/v1"
            // string model = "microsoft/Phi-4-mini-reasoning:featherless-ai";//"nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free";
            // string apiKey=;
            // string provider = "huggingface";//"openrouter"

            string urlInference = "https://openrouter.ai/api/v1";
            string model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free";
            string apiKey=_apiKey;
            string provider = "openrouter";


            var kernel =builder.AddOpenAIChatCompletion(model, new Uri(urlInference), apiKey, string.Empty,provider , null).Build();
             //.AddOpenAIChatCompletion("google/gemma-4-26b-a4b-it:free", new Uri("https://openrouter.ai/api/v1"), _apiKey, string.Empty, "openrouter", null) .Build();
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
                var result = await chat.GetChatMessageContentAsync(chatHistory,executionSettings: settings, kernel: kernel, cancellationToken: cancellationToken).ConfigureAwait(false);
                rawResponse = result?.Content ?? string.Empty;
               // var plan = ParseActionPlan(rawResponse);
                finalResult = TakeAction(rawResponse).Result;
                // Console.WriteLine($"{result?.Content}");
                // history.Add($"Thought: {plan.Thought}");
                // history.Add($"Action: {plan.Action}");
                // history.Add($"ActionInput: {plan.ActionInput}");

                // if (string.Equals(plan.Action, "DB_SCHEMA", StringComparison.OrdinalIgnoreCase))
                // {
                //     var observation = DatabaseQueryTool.BuildSchemaContext(schema);
                //     history.Add("Observation: schema details returned");
                //     history.Add(observation);
                //     continue;
                // }

                // if (string.Equals(plan.Action, "WRITE_SQL", StringComparison.OrdinalIgnoreCase))
                // {
                //     finalSql = CleanSql(plan.ActionInput);
                //     history.Add($"Observation: '{finalSql}'");
                //     //continue;
                //     break;
                // }


                // if (string.Equals(plan.Action, "Execute_SQL", StringComparison.OrdinalIgnoreCase))
                // {
                //     finalSql = CleanSql(plan.ActionInput);
                //     finalResult = await executeSql(finalSql);
                //     history.Add($"Observation: '{finalResult}'");
                //     //continue;
                //     break;
                // }

                // if (string.Equals(plan.Action, "FINISH", StringComparison.OrdinalIgnoreCase))
                // {
                //     finalSql = CleanSql(plan.ActionInput);
                //     break;
                // }

                // history.Add($"Observation: Unknown action '{plan.Action}'.");
            }

            // if (string.IsNullOrWhiteSpace(finalSql) && !string.IsNullOrWhiteSpace(rawResponse))
            // {
            //     finalSql = CleanSql(rawResponse);
            //     history.Add("Observation: Parsed raw response as SQL fallback.");
            // }

            return new ReasoningResult(finalResult ?? string.Empty, finalSql ?? string.Empty, rawResponse, history);
        }

private static async Task<string> TakeAction(string rawResponse)
        {
        var builder = Kernel.CreateBuilder();
        
        // Point to your local xLAM instance (vLLM, Ollama, etc.)
        // builder.AddOpenAIChatCompletion(
        //     modelId: "xlam-1b-fc-r",
        //     apiKey: "local",
        //     endpoint: new Uri("http://localhost:8000/v1")
        // );

            string urlInference = "https://router.huggingface.co/v1";// "https://openrouter.ai/api/v1"
            string model = "affanshaikhsurab/qwen3_0.6b_xlam_function_calling:featherless-ai";//"nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free";
            string apiKey = Environment.GetEnvironmentVariable("HUGGINGFACE_API_KEY");
            string provider = "huggingface";//"openrouter"

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("HUGGINGFACE_API_KEY environment variable is required for action selection.");
            }
            
            var kernel =builder.AddOpenAIChatCompletion(model, new Uri(urlInference), apiKey, string.Empty,provider , null).Build();
            



        //Kernel kernel = builder.Build();
        kernel.Plugins.AddFromType<DatabasePlugin>("DatabaseTools");

        var xlamService = kernel.GetRequiredService<IChatCompletionService>();

        // 3. Mock Phi-4 reasoning output (Multiple Steps)
        //string[] phi4Steps = 
        // {
        //     "First, get the historical price for AAPL.",
        //     "Then, calculate the percentage change from 140 to the returned price."
        // };
        Console.WriteLine("Parsing steps from Phi-4 response..."+rawResponse);
        rawResponse ="[ {\"step\":1,\"content\":\"Identify the table name that stores problem records in the SalesforceCoder database.\"},{\"step\":2,\"content\":\"Retrieve a single problem record, for example the most recent one, from the identified table.\"},{\"step\":3,\"content\":\"Extract the solution field value from the retrieved problem row.\"},{\"step\":4,\"content\":\"Return the extracted solution as the final answer.\"}]";
        List<XLamStep> phi4Steps = ParseSteps(rawResponse);
        var contextMemory = "";//new Dictionary<string, string>();

        if(phi4Steps.Count == 0)
        {
            Console.WriteLine("No steps parsed from Phi-4 response. Returning raw response.");
            return rawResponse;
        }
        string toolsJson = SerializeToolsMetadata(kernel);
        Console.WriteLine($"Available Tools Metadata: {toolsJson}");
          
        // 4. Iterate through steps for Action Identification
        foreach (var step in phi4Steps)
        {
            Console.WriteLine($"\n============================ Processing Step: {step.step} =============================");

            // Get metadata of all registered functions to help xLAM identify tools
            //   string xlamPrompt = $@"
// Available Tools: {toolsJson}
// Current Step: {step.content}
// Previous Results: {JsonSerializer.Serialize(contextMemory)}
// Task: Return ONLY a JSON object with 'PluginName', 'FunctionName', and 'Arguments' (key-value pairs).";
StringBuilder pbuilder = new StringBuilder();
            pbuilder.AppendLine("You are an intelligent agent that identifies which tool to use for a given task.");
            pbuilder.AppendLine("Based on the current step and available tools, determine the best action to take.");
            pbuilder.AppendLine();
            pbuilder.AppendLine($@"Available Tools: {toolsJson}");
            pbuilder.AppendLine();
            pbuilder.AppendLine($@"Current Step: {step.content}");
            pbuilder.AppendLine();
            pbuilder.AppendLine($@"Previous Results:{contextMemory}");//{JsonSerializer.Serialize(contextMemory)}
            pbuilder.AppendLine();
            pbuilder.AppendLine("CRITICAL RESPONSE FORMAT:");
            pbuilder.AppendLine("<CRITICAL_INSTRUCTION>");
            pbuilder.AppendLine("1. Respond with ONLY the JSON array.");
            pbuilder.AppendLine("2. Do NOT include any text before or after the JSON.");
            pbuilder.AppendLine("3. Do NOT use markdown code blocks (```json ... ```).");
            pbuilder.AppendLine("4. Actions sequentially as an array of objects with 'PluginName', 'FunctionName', and 'Arguments' (key-value pairs).");
            pbuilder.AppendLine("</CRITICAL_INSTRUCTION>");
            pbuilder.AppendLine("Task: Return ONLY a JSON object with 'PluginName', 'FunctionName', and 'Arguments' (key-value pairs).");

            string xlamPrompt= pbuilder.ToString();
            Console.WriteLine($"xLAM Prompt:\n{xlamPrompt}");
            var response = await xlamService.GetChatMessageContentAsync(xlamPrompt);

            try
            {
                Console.WriteLine($"xLAM Response: {response}");
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var responseText = response.ToString()!;
                ActionCall action = null;

                // Try to deserialize as array first
                try
                {
                    var actionArray = JsonSerializer.Deserialize<List<ActionCall>>(responseText, options);
                    if (actionArray != null && actionArray.Count > 0)
                    {
                        action = actionArray[0];
                        Console.WriteLine($"Parsed action from array: {action.PluginName}.{action.FunctionName}");
                    }
                }
                catch
                {
                    // If array deserialization fails, try direct object deserialization
                    action = JsonSerializer.Deserialize<ActionCall>(responseText, options);
                    Console.WriteLine($"Parsed action as object: {action.PluginName}.{action.FunctionName}");
                }

                if (action != null && !string.IsNullOrEmpty(action.PluginName) && !string.IsNullOrEmpty(action.FunctionName))
                {
                    // 5. Dynamic Execution via Semantic Kernel
                    Console.WriteLine($"Executing: {action.PluginName}.{action.FunctionName}");
                    
                    // Map the Dictionary arguments into KernelArguments
                    var kArgs = new KernelArguments(action.Arguments ?? action.Parameters ?? new Dictionary<string, object>());
                    var result = await kernel.InvokeAsync(action.PluginName, action.FunctionName, kArgs);

                    Console.WriteLine($"Result: {result}");
                    contextMemory +=@$"{step.content} + action output: {result.ToString()}";
                    //contextMemory[step.content] = result.ToString();
                }
                else
                {
                    Console.WriteLine($"Action missing required fields");
                   // contextMemory[step.content] = "Action missing PluginName or FunctionName";
                    contextMemory+= @$"{step.content} + action output: Action missing PluginName or FunctionName";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing step or deserializing action: {ex.Message}");
                contextMemory+= @$"{step.content} + action output: Error: {ex.Message}";
                break;
            }
        }
        return contextMemory;  //contextMemory.ContainsKey(phi4Steps[^1].content) ? contextMemory[phi4Steps[^1].content] : "No result from final step.";
    }

    private static string SerializeToolsMetadata(Kernel kernel)
    {
        try
        {
            var toolsMetadata = kernel.Plugins.GetFunctionsMetadata();
            Console.WriteLine($"Retrieved {toolsMetadata.Count} functions from kernel metadata.");
             foreach (var func in toolsMetadata)
            {
                Console.WriteLine($"Function: {func.Name}, Plugin: {func.PluginName}, Description: {func.Description}");
                if (func.Parameters != null)
                {
                    foreach (var param in func.Parameters)
                    {
                        Console.WriteLine($"\tParameter: {param.Name}, Type: {param.ParameterType}, Description: {param.Description}");
                    }
                }
            }
            // Create a simplified representation of tools
            var simplifiedTools = new List<Dictionary<string, object>>();
            
            foreach (var func in toolsMetadata)
            {
                var parametersList = func.Parameters != null
                    ? func.Parameters.Select(p => new 
                    { 
                        p.Name, 
                        p.Description, 
                        ParameterType = p.ParameterType?.Name ?? "object" 
                    }).Cast<object>().ToList()
                    : new List<object>();

                simplifiedTools.Add(new Dictionary<string, object>
                {
                    { "PluginName", func.PluginName ?? "Unknown" },
                    { "FunctionName", func.Name ?? "Unknown" },
                    { "Description", func.Description ?? "No description" },
                    { "Parameters", parametersList }
                });
            }
            
            var options = new JsonSerializerOptions { WriteIndented = false };
            return JsonSerializer.Serialize(simplifiedTools, options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error serializing tools metadata: {ex.Message}");
            return "[]"; // Return empty array if serialization fails
        }
    }

        
    public static List<XLamStep> ParseSteps(string rawPlan)
    {
        var steps = new List<XLamStep>();

        if (string.IsNullOrWhiteSpace(rawPlan))
        {
            return steps;
        }

        // Trim the response
        string trimmed = rawPlan.Trim();

        // Try 1: Direct JSON array (no code fences)
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                steps = JsonSerializer.Deserialize<List<XLamStep>>(trimmed, options) ?? new List<XLamStep>();
                
                Console.WriteLine("Successfully parsed direct JSON array.");
                foreach (var step in steps)
                {
                    Console.WriteLine($"Step {step.step}: {step.content}");
                }
                return steps;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to parse direct JSON: {ex.Message}");
            }
        }

        // Try 2: JSON wrapped in code fences
        string jsonPatternWithFences = @"```(?:json)?\s*([\s\S]*?)\s*```";
        var jsonMatch = Regex.Match(trimmed, jsonPatternWithFences, RegexOptions.IgnoreCase);

        if (jsonMatch.Success)
        {
            try
            {
                string jsonContent = jsonMatch.Groups[1].Value.Trim();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                steps = JsonSerializer.Deserialize<List<XLamStep>>(jsonContent, options) ?? new List<XLamStep>();
                
                Console.WriteLine("Successfully parsed JSON from code fences.");
                foreach (var step in steps)
                {
                    Console.WriteLine($"Step {step.step}: {step.content}");
                }
                return steps;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to parse JSON from fences: {ex.Message}");
            }
        }

        // Try 3: Extract JSON array from text
        int arrayStart = trimmed.IndexOf('[');
        int arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            try
            {
                string jsonContent = trimmed[arrayStart..(arrayEnd + 1)];
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                steps = JsonSerializer.Deserialize<List<XLamStep>>(jsonContent, options) ?? new List<XLamStep>();
                
                Console.WriteLine("Successfully extracted and parsed JSON array from text.");
                foreach (var step in steps)
                {
                    Console.WriteLine($"Step {step.step}: {step.content}");
                }
                return steps;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Failed to extract and parse JSON: {ex.Message}");
            }
        }

        // Fall back to markdown-style step parsing
        Console.WriteLine("Falling back to markdown step parsing...");
        string stepHeaderPattern = @"(?m)^(?:\s*#{1,6}\s*)?Step\s+(\d+)\s*:\s*(.+)$";
        var matches = Regex.Matches(trimmed, stepHeaderPattern, RegexOptions.IgnoreCase);

        if (matches.Count > 0)
        {
            for (int i = 0; i < matches.Count; i++)
            {
                int stepNumber = int.Parse(matches[i].Groups[1].Value);
                string stepTitle = matches[i].Groups[2].Value.Trim();
                int contentStart = matches[i].Index + matches[i].Length;
                int contentEnd = (i + 1 < matches.Count) ? matches[i + 1].Index : trimmed.Length;
                string stepContent = trimmed[contentStart..contentEnd].Trim();

                if (!string.IsNullOrEmpty(stepContent))
                {
                    stepContent = Regex.Replace(stepContent, @"```[\s\S]*?```", string.Empty, RegexOptions.Singleline).Trim();
                    stepContent = Regex.Replace(stepContent, @"\s+", " ").Trim();
                }

                string content = string.IsNullOrWhiteSpace(stepContent)
                    ? stepTitle
                    : stepTitle + ": " + stepContent;

                steps.Add(new XLamStep
                {
                    step = stepNumber,
                    content = content
                });
            }

            Console.WriteLine("Successfully parsed markdown steps.");
            foreach (var step in steps)
            {
                Console.WriteLine($"Step {step.step}: {step.content}");
            }
        }
        else
        {
            Console.WriteLine("Warning: No steps found in response. Returning empty list.");
        }

        return steps;
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


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
using Aimitra.Security.Guardrails;
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
        private readonly IReadOnlyList<Topic> _topics;

        public SemanticKernelOrchestrator(string routeAgent, string apiKey, string model, string endpoint, string presidioEndpoint)
            : this(routeAgent, apiKey, model, endpoint, presidioEndpoint, topics: null) { }

        public SemanticKernelOrchestrator(
            string routeAgent,
            string apiKey,
            string model,
            string endpoint,
            string presidioEndpoint,
            IReadOnlyList<Topic>? topics)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? throw new ArgumentException("API key cannot be empty.", nameof(apiKey)) : apiKey;
            _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("Model cannot be empty.", nameof(model)) : model;
            _endpoint = new Uri(endpoint);
            _presidioEndpoint = string.IsNullOrWhiteSpace(presidioEndpoint) ? throw new ArgumentException("Presidio endpoint cannot be empty.", nameof(presidioEndpoint)) : presidioEndpoint;
            _pluginLoader = new KernelPluginLoader(KernelPluginOptions.FromEnvironment());
            _routeAgent = string.IsNullOrWhiteSpace(routeAgent) ? throw new ArgumentException("Route agent cannot be empty.", nameof(routeAgent)) : routeAgent;
            _topics = topics ?? Array.Empty<Topic>();
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
            builder.Services.AddSingleton<IPromptRenderFilter>(maskingEngine);

            // Always-on guardrails: content safety + prompt injection — registered at every filter point
            GuardrailService.Register(builder);

            var kernel =builder.AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty,provider , null).Build();
            Console.WriteLine("Kernel built with OpenAI Chat Completion service.");

            // Expose guardrails as LLM-callable tools (matches tools_sent in every agent step of the trace)
            kernel.Plugins.Add(GuardrailService.CreatePlugin());
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
                prompt =maskingEngine.maskPrompt(prompt).Result;
                chatHistory.AddUserMessage(prompt);

                ChatMessageContent result;
                try
                {
                    result = await chat.GetChatMessageContentAsync(chatHistory, executionSettings: settings, kernel: kernel, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (GuardrailViolationException gex)
                {
                    Console.WriteLine($"[Guardrail] Request blocked: {gex.Message}");
                    return new ReasoningResult(string.Empty, string.Empty,
                        $"Request blocked by safety guardrail [{gex.Result.ViolationType}]: {gex.Result.Reason}", history);
                }

                rawResponse = result?.Content ?? string.Empty;
                Console.WriteLine("Parsing steps response..." + rawResponse);
                rawResponse = maskingEngine.unmaskResult(rawResponse).Result;
            }

 
            return new ReasoningResult(finalResult ?? string.Empty, finalSql ?? string.Empty, rawResponse, history);
        }

        // ----------------------------------------------------------------
        // Topic routing — SELECT the right Topic for a user message
        // ----------------------------------------------------------------

        /// <summary>
        /// Uses the configured LLM to select the best matching <see cref="Topic"/> for
        /// <paramref name="userPrompt"/> by offering each topic as a callable routing function.
        ///
        /// The LLM calls exactly one function — that invocation IS the routing decision.
        /// This mirrors the trace's topic_selector agent which routes by tool-call, not
        /// by embedding similarity alone.
        /// </summary>
        public async Task<Topic?> SelectTopicAsync(
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (_topics.Count == 0) return null;

            string? selectedTopicName = null;

            // Routing kernel — LLM + one KernelFunction per topic, no domain tools
            var builder = Kernel.CreateBuilder();
            GuardrailService.Register(builder);
            var routingKernel = builder
                .AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty, "OpenAI", null)
                .Build();

            // One routing function per topic; closure captures the name when called
            var routingFunctions = new List<KernelFunction>();
            foreach (var topic in _topics)
            {
                var capturedName = topic.Name;
                routingFunctions.Add(KernelFunctionFactory.CreateFromMethod(
                    method:       () => { selectedTopicName = capturedName; return capturedName; },
                    functionName: Regex.Replace(capturedName, @"[^a-zA-Z0-9_]", "_"),
                    description:  topic.Description));
            }

            routingKernel.Plugins.Add(
                KernelPluginFactory.CreateFromFunctions("TopicRouter",
                    description: "Routes the user message to the correct topic.",
                    functions:   routingFunctions));

            var chat = routingKernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory(
                "You are a topic selector. Call the single function whose description best " +
                "matches the user's message. Call exactly one function then stop. Do not use your own data for answering alway look for data from prompt itself or tools in topics.");
            history.AddUserMessage(userPrompt);

            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                MaxTokens = 200
            };

            await chat.GetChatMessageContentAsync(
                history,
                executionSettings: settings,
                kernel: routingKernel,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return _topics.FirstOrDefault(t => t.Name == selectedTopicName);
        }

        // ----------------------------------------------------------------
        // Scoped execution — RUN with only the selected topic's Actions
        // ----------------------------------------------------------------

        /// <summary>
        /// Builds a kernel scoped to <paramref name="topic"/> and executes the ReAct loop
        /// using only that topic's <see cref="Topic.Actions"/> plugins.
        ///
        /// The model cannot see tools from other topics. Guardrails and PII masking are
        /// always registered regardless of topic, matching the trace pattern where every
        /// agent's tools_sent list includes Inappropriate_Content and Prompt_Injection.
        /// </summary>
        public async Task<string> RunWithTopicAsync(
            string userPrompt,
            Topic topic,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(30000);
            if (topic == null) throw new ArgumentNullException(nameof(topic));

            var maskingEngine = new PiiMaskingEngine(_presidioEndpoint);

            var builder = Kernel.CreateBuilder();

            // PII masking — always
            builder.Services.AddSingleton<IFunctionInvocationFilter>(maskingEngine);
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(maskingEngine);
            builder.Services.AddSingleton<IPromptRenderFilter>(maskingEngine);

            // Guardrails — always
            GuardrailService.Register(builder);

            var scopedKernel = builder
                .AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty, "OpenAI", null)
                .Build();

            scopedKernel.Plugins.Add(GuardrailService.CreatePlugin());

            // Only this topic's Actions — no other topic's tools visible
            foreach (var plugin in topic.Actions)
                scopedKernel.Plugins.Add(plugin);

            var chat = scopedKernel.GetRequiredService<IChatCompletionService>();

            var chatHistory = new ChatHistory(
                $"Active topic: {topic.Name}. {topic.Description}\n" +
                "Use the available tools to fulfil the user's request.");

            var maskedPrompt = await maskingEngine.maskPrompt(userPrompt).ConfigureAwait(false);
            chatHistory.AddUserMessage(maskedPrompt);

            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                MaxTokens = 1000
            };

            try
            {
                var result = await chat.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings: settings,
                    kernel: scopedKernel,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                var response = result?.Content ?? string.Empty;
                return await maskingEngine.unmaskResult(response).ConfigureAwait(false);
            }
            catch (GuardrailViolationException gex)
            {
                Console.WriteLine($"[Guardrail] Topic '{topic.Name}' blocked: {gex.Message}");
                return $"Request blocked by safety guardrail [{gex.Result.ViolationType}]: {gex.Result.Reason}";
            }
        }

        // ----------------------------------------------------------------
        // Combined: route → scoped execute
        // ----------------------------------------------------------------

        /// <summary>
        /// Selects the best <see cref="Topic"/> for <paramref name="userPrompt"/> and
        /// executes the ReAct loop with only that topic's Actions visible to the model.
        ///
        /// Returns the selected topic alongside the response so the caller can log the
        /// routing decision (the TransitionStep equivalent from the trace).
        /// Falls back to <see cref="GenerateSqlFromQuestionAsync"/> behaviour when no
        /// topics are registered.
        /// </summary>
        public async Task<(Topic? SelectedTopic, string Response)> RunTopicRoutedAsync(
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            var topic = await SelectTopicAsync(userPrompt, cancellationToken).ConfigureAwait(false);

            if (topic == null)
            {
                Console.WriteLine("[TopicRouter] No topic matched — no topics registered.");
                return (null, "No topic could be selected for this request.");
            }

            Console.WriteLine($"[TopicRouter] Selected topic: '{topic.Name}'");
            var response = await RunWithTopicAsync(userPrompt, topic, cancellationToken).ConfigureAwait(false);
            return (topic, response);
        }

    }
}


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

        /// <summary>The topics registered with this orchestrator.</summary>
        public IReadOnlyList<Topic> Topics => _topics;

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
        /// Uses the configured LLM to select the single best matching <see cref="Topic"/>.
        /// Delegates to <see cref="SelectTopicsAsync"/> and returns the first result.
        /// </summary>
        public async Task<Topic?> SelectTopicAsync(
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            var topics = await SelectTopicsAsync(userPrompt, cancellationToken).ConfigureAwait(false);
            return topics.Count > 0 ? topics[0] : null;
        }

        /// <summary>
        /// Uses the configured LLM to select ALL topics needed to fully answer
        /// <paramref name="userPrompt"/>, returned in the order they should be executed.
        ///
        /// For a query like "predict the future of the top scorer in the database" the
        /// LLM will call two routing functions — DatabaseTools first (to look up the name)
        /// then AstrologerPlugin second (to generate the prediction). Each function-call
        /// IS the routing decision; call order becomes execution order.
        /// </summary>
        public async Task<IReadOnlyList<Topic>> SelectTopicsAsync(
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            if (_topics.Count == 0) return Array.Empty<Topic>();

            var selectedNames = new List<string>();

            // Routing kernel — LLM + one KernelFunction per topic, no domain tools
            var builder = Kernel.CreateBuilder();
            GuardrailService.Register(builder);
            var routingKernel = builder
                .AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty, "OpenAI", null)
                .Build();

            // Each routing function appends its name to selectedNames in call order
            var routingFunctions = new List<KernelFunction>();
            foreach (var topic in _topics)
            {
                var capturedName = topic.Name;
                routingFunctions.Add(KernelFunctionFactory.CreateFromMethod(
                    method:       () => { if (!selectedNames.Contains(capturedName)) selectedNames.Add(capturedName); return capturedName; },
                    functionName: Regex.Replace(capturedName, @"[^a-zA-Z0-9_]", "_"),
                    description:  topic.Description));
            }

            routingKernel.Plugins.Add(
                KernelPluginFactory.CreateFromFunctions("TopicRouter",
                    description: "Routes the user message to the required topics.",
                    functions:   routingFunctions));

            var chat = routingKernel.GetRequiredService<IChatCompletionService>();
            var history = new ChatHistory(
                "You are a topic router. Analyse the user request carefully.\n" +
                "• If the request can be answered by a SINGLE topic, call that one function.\n" +
                "• If the request SPANS multiple topics (e.g. look up data from a database " +
                "AND THEN use that result for a prediction or other action), call ALL required " +
                "topic functions IN THE ORDER they must be executed — the output of each step " +
                "feeds into the next.\n" +
                "Do NOT produce any text — only function calls.");
            history.AddUserMessage(userPrompt);

            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                MaxTokens = 300
            };

            // SK's Auto function-call loop runs until the LLM stops calling functions
            await chat.GetChatMessageContentAsync(
                history,
                executionSettings: settings,
                kernel: routingKernel,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // Preserve call order
            return _topics
                .Where(t => selectedNames.Contains(t.Name))
                .OrderBy(t => selectedNames.IndexOf(t.Name))
                .ToList();
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
            Console.WriteLine($"Running with topic with 20sec wait: {topic.Name}");
            await Task.Delay(20000);
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
        // Combined: route → chain execute → synthesise
        // ----------------------------------------------------------------

        /// <summary>
        /// Selects all required <see cref="Topic"/>s for <paramref name="userPrompt"/>,
        /// executes them in order (each step receives the accumulated context from
        /// previous steps), then synthesises a final answer when more than one topic
        /// was involved.
        ///
        /// Example: "predict the future of the top scorer in the salesforcecoder leaderboard"
        ///   Step 1 — DatabaseTools: fetches the top scorer's name from the DB.
        ///   Step 2 — AstrologerPlugin: uses that name to generate the prediction.
        ///   Synthesis: combines both results into a coherent final answer.
        /// </summary>
        public async Task<(IReadOnlyList<Topic> SelectedTopics, string Response)> RunTopicRoutedAsync(
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            var topics = await SelectTopicsAsync(userPrompt, cancellationToken).ConfigureAwait(false);

            if (topics.Count == 0)
            {
                Console.WriteLine("[TopicRouter] No topic matched — no topics registered.");
                return (Array.Empty<Topic>(), "No topic could be selected for this request.");
            }

            if (topics.Count == 1)
            {
                Console.WriteLine($"[TopicRouter] Single topic: '{topics[0].Name}'");
                var resp = await RunWithTopicAsync(userPrompt, topics[0], cancellationToken).ConfigureAwait(false);
                return (topics, resp);
            }

            // --- Multi-topic chaining ---
            Console.WriteLine($"[TopicRouter] Multi-topic pipeline ({topics.Count} steps): {string.Join(" → ", topics.Select(t => t.Name))}");

            var stepResults = new List<(string TopicName, string Result)>();

            for (int i = 0; i < topics.Count; i++)
            {
                await Task.Delay(20000);
                var topic = topics[i];
                Console.WriteLine($"[TopicRouter] Step {i + 1}/{topics.Count}: '{topic.Name}'");

                // First step uses original prompt; subsequent steps append accumulated context
                string stepPrompt = i == 0
                    ? userPrompt
                    : BuildChainedPrompt(userPrompt, stepResults);

                var stepResult = await RunWithTopicAsync(stepPrompt, topic, cancellationToken).ConfigureAwait(false);
                stepResults.Add((topic.Name, stepResult));

                Console.WriteLine($"[TopicRouter] Step {i + 1} result: {stepResult}");
            }

            // Synthesise a single coherent answer from all step results
            var finalResponse = await SynthesizeResponseAsync(userPrompt, stepResults, cancellationToken).ConfigureAwait(false);
            return (topics, finalResponse);
        }

        /// <summary>
        /// Builds the prompt for step N (1-based) of a multi-topic chain by appending the
        /// results gathered so far so the model has full context.
        /// </summary>
        private static string BuildChainedPrompt(
            string originalPrompt,
            IReadOnlyList<(string TopicName, string Result)> priorResults)
        {
            var sb = new StringBuilder();
            sb.AppendLine(originalPrompt);
            sb.AppendLine();
            sb.AppendLine("--- Context gathered by earlier steps ---");
            foreach (var (name, result) in priorResults)
                sb.AppendLine($"[{name}]: {result}");
            sb.AppendLine("--- Use the above context to complete your part of the task ---");
            return sb.ToString();
        }

        /// <summary>
        /// Calls the LLM to produce one coherent final answer that integrates the results
        /// from all chained topic steps.
        /// </summary>
        private async Task<string> SynthesizeResponseAsync(
            string originalPrompt,
            IReadOnlyList<(string TopicName, string Result)> stepResults,
            CancellationToken cancellationToken = default)
        {
            var builder = Kernel.CreateBuilder();
            var synthesisKernel = builder
                .AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty, "OpenAI", null)
                .Build();

            var chat = synthesisKernel.GetRequiredService<IChatCompletionService>();

            var contextBlock = new StringBuilder();
            foreach (var (name, result) in stepResults)
                contextBlock.AppendLine($"[{name}]: {result}");
            Console.WriteLine("========================================================= \n"+
                                "Synthesizing final response from context:\n" +
                                "========================================================= \n" 
                                );    
            var history = new ChatHistory(
                "You are a helpful assistant. You have been given the results of a multi-step " +
                "pipeline that was executed to answer the user's request. " +
                "Write a single, clear, complete response that combines all the information. " +
                "Do not mention the pipeline or the step names. Do not include any of the internal thought process, only the final answer for the user.");
            history.AddUserMessage(
                $"Original request: {originalPrompt}\n\n" +
                $"Step results:\n{contextBlock}");

            var settings = new OpenAIPromptExecutionSettings { MaxTokens = 1000 };

            try
            {
                var result = await chat.GetChatMessageContentAsync(
                    history,
                    executionSettings: settings,
                    kernel: synthesisKernel,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                return result?.Content ?? string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Synthesis] Failed: {ex.Message}");
                // Fallback: concatenate step results
                return string.Join("\n\n", stepResults.Select(r => $"{r.TopicName}: {r.Result}"));
            }
        }

    }
}


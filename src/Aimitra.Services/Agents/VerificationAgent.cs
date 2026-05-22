using System;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
//using Aimitra.SamplePlugins.Plugins;
using Aimitra.Security;
using Aimitra.Security.Guardrails;
using Aimitra.Services.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Aimitra.Services.Agents
{
    /// <summary>
    /// Verification domain agent.
    ///
    /// Responsibilities:
    /// <list type="bullet">
    ///   <item>Asks the customer for their account number and SSN last-4.</item>
    ///   <item>Calls <c>verify_customer</c> via <see cref="VerificationPlugin"/>.</item>
    ///   <item>Populates <see cref="ConversationState"/> with the customer profile on success.</item>
    ///   <item>Hands off to <c>topic_selector</c> via <c>go_back</c> so the router can
    ///         continue with the customer's original request.</item>
    /// </list>
    ///
    /// This agent uses a scoped Semantic Kernel that only has the verification and
    /// navigation tools registered — it cannot see billing, technical, or plan tools.
    /// </summary>
    public sealed class VerificationAgent : ITopicAgent
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly Uri _endpoint;
        private readonly string _presidioEndpoint;

        /// <inheritdoc/>
        public string TopicName => "verification";

        /// <inheritdoc/>
        public string Description =>
            "Verifies the customer's identity before allowing access to account-sensitive topics. " +
            "Must be the first step for any request that requires account information.";

        /// <param name="apiKey">LLM API key (passed through to the OpenAI-compatible endpoint).</param>
        /// <param name="model">Model identifier (e.g. <c>gpt-4o</c>).</param>
        /// <param name="endpoint">Base URL of the OpenAI-compatible chat completion endpoint.</param>
        /// <param name="presidioEndpoint">URL of the running Presidio analyser/anonymiser service.</param>
        public VerificationAgent(string apiKey, string model, string endpoint, string presidioEndpoint)
        {
            if (string.IsNullOrWhiteSpace(apiKey))      throw new ArgumentException("API key is required.",        nameof(apiKey));
            if (string.IsNullOrWhiteSpace(model))       throw new ArgumentException("Model is required.",          nameof(model));
            if (string.IsNullOrWhiteSpace(endpoint))    throw new ArgumentException("Endpoint is required.",       nameof(endpoint));
            if (string.IsNullOrWhiteSpace(presidioEndpoint)) throw new ArgumentException("Presidio endpoint is required.", nameof(presidioEndpoint));

            _apiKey           = apiKey;
            _model            = model;
            _endpoint         = new Uri(endpoint);
            _presidioEndpoint = presidioEndpoint;
        }

        /// <inheritdoc/>
        public async Task<AgentTransition> RunAsync(
            string userInput,
            ConversationState state,
            CancellationToken cancellationToken = default)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            // ── Build a scoped kernel ─────────────────────────────────────────
            var nav        = new NavigationPlugin();
            var verPlugin  = new VerificationPlugin(state);
            var masker     = new PiiMaskingEngine(_presidioEndpoint);

            var builder = Kernel.CreateBuilder();

#pragma warning disable SKEXP0001
            builder.Services.AddSingleton<IFunctionInvocationFilter>(masker);
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(masker);
#pragma warning restore SKEXP0001
            builder.Services.AddSingleton<IPromptRenderFilter>(masker);

            GuardrailService.Register(builder);

            var kernel = builder
                .AddOpenAIChatCompletion(_model, _endpoint, _apiKey, string.Empty, "OpenAI", null)
                .Build();

            // Guardrails as LLM-callable tools (matches trace pattern)
            kernel.Plugins.Add(GuardrailService.CreatePlugin());

            // Verification tool — only tool that can touch customer data
            kernel.Plugins.AddFromObject(verPlugin, "VerificationTools");

            // go_back — lets the LLM hand off to another topic
            kernel.Plugins.AddFromObject(nav, "NavigationTools");

            // ── Execution settings ────────────────────────────────────────────
            var settings = new OpenAIPromptExecutionSettings
            {
                MaxTokens   = 1000,
                Temperature = 0.3,
#pragma warning disable SKEXP0001
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
#pragma warning restore SKEXP0001
            };

            // ── Build chat history ────────────────────────────────────────────
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(BuildSystemPrompt(state));

            var maskedInput = await masker.maskPrompt(userInput).ConfigureAwait(false);
            chatHistory.AddUserMessage(maskedInput);

            // ── Call the LLM ──────────────────────────────────────────────────
            ChatMessageContent result;
            try
            {
                result = await kernel
                    .GetRequiredService<IChatCompletionService>()
                    .GetChatMessageContentAsync(chatHistory, settings, kernel, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GuardrailViolationException gex)
            {
                return new AgentTransition
                {
                    FromAgent      = TopicName,
                    ToAgent        = "topic_selector",
                    TransitionType = "fallback",
                    TriggerAction  = "guardrail",
                    AgentResponse  = $"Your request was blocked by a safety guardrail " +
                                     $"[{gex.Result.ViolationType}]: {gex.Result.Reason}"
                };
            }

            var rawResponse = result?.Content ?? string.Empty;
            var response    = await masker.unmaskResult(rawResponse).ConfigureAwait(false);

            // ── Determine next topic ──────────────────────────────────────────
            // Prefer an explicit go_back call from the LLM; if the customer is now
            // verified but the LLM forgot to call go_back, hand off automatically.
            var nextTopic = nav.ConsumeNextTopic();

            if (string.IsNullOrEmpty(nextTopic) && state.CustomerVerified)
                nextTopic = "topic_selector";

            return new AgentTransition
            {
                FromAgent      = TopicName,
                ToAgent        = nextTopic,
                TransitionType = nextTopic != null ? "handoff" : "complete",
                TriggerAction  = state.CustomerVerified ? "verify_customer" : "pending_verification",
                AgentResponse  = response
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string BuildSystemPrompt(ConversationState state)
        {
            var alreadyVerified = state.CustomerVerified
                ? $"The customer is already verified as {state.CustomerName} (ID: {state.CustomerId}). " +
                  "Call go_back with nextTopic='topic_selector' immediately."
                : "The customer has NOT been verified yet.";

            return @"""
                You are the Verification Agent for Apex Telecom. Your sole responsibility is to
                confirm the identity of the customer before any account-sensitive action is taken.

                {alreadyVerified}

                --- WORKFLOW ---
                1. Greet the customer and explain that you need to verify their identity.
                2. Ask for their account number AND the last 4 digits of their SSN.
                3. Once you have both, call verify_customer(accountNumber, ssnLast4).
                4. If verification succeeds:
                   - Warmly confirm their name (e.g. ""Great, I've verified your identity, Jane!"").
                   - Call go_back(nextTopic= ""topic_selector "") to return to the main router.
                5. If verification fails:
                   - Apologise and ask the customer to double-check their details.
                   - Offer one more attempt, then advise them to contact support if it fails again.

                --- CONSTRAINTS ---
                - Do NOT discuss billing, plans, internet issues, or account changes.
                - Do NOT reveal SSN digits back to the customer.
                - Do NOT proceed with any sensitive action until verify_customer returns success=true.

                Session locale: {state.Locale}
                """;
        }
    }
}

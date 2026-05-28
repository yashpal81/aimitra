using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
using Aimitra.Services.Orchestration;

namespace Aimitra.Services.Orchestration
{
    /// <summary>
    /// Stateful multi-turn orchestrator that wraps <see cref="SemanticKernelOrchestrator"/>
    /// and adds cross-turn, cross-agent conversation state.
    ///
    /// Responsibilities:
    /// <list type="bullet">
    ///   <item>Maintains a single <see cref="ConversationState"/> for the session lifetime.</item>
    ///   <item>Records every <see cref="AgentTransition"/> for audit.</item>
    ///   <item>Injects the conversation transcript and verified-customer facts into every
    ///         prompt so the LLM has full context without being re-asked.</item>
    ///   <item>Delegates routing and LLM execution to <see cref="SemanticKernelOrchestrator"/>.</item>
    ///   <item>Dispatches to a registered <see cref="ITopicAgent"/> when one exists for the
    ///         selected topic; falls back to the kernel orchestrator otherwise.</item>
    ///   <item>Follows <c>go_back</c> / handoff directives returned by agents.</item>
    /// </list>
    /// </summary>
    public sealed class TopicOrchestrator
    {
        private const int MaxHandoffsPerTurn = 5;
        private const int TranscriptWindowTurns = 6;

        private readonly SemanticKernelOrchestrator _kernel;
        private readonly Dictionary<string, ITopicAgent> _agentRegistry;
        private readonly List<AgentTransition> _transitionLog = new();

        /// <summary>Shared mutable state for this session.</summary>
        public ConversationState State { get; } = new();

        /// <summary>Ordered log of every agent transition this session.</summary>
        public IReadOnlyList<AgentTransition> TransitionLog => _transitionLog;

        /// <param name="kernel">
        /// Configured <see cref="SemanticKernelOrchestrator"/> used for routing and
        /// LLM execution when no registered <see cref="ITopicAgent"/> is found.
        /// </param>
        /// <param name="agents">
        /// Optional domain-agent implementations. When a topic is selected by the
        /// router and a matching agent is registered here, <see cref="ITopicAgent.RunAsync"/>
        /// is called instead of the generic kernel path.
        /// </param>
        public TopicOrchestrator(
            SemanticKernelOrchestrator kernel,
            IEnumerable<ITopicAgent>? agents = null)
        {
             
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _agentRegistry = agents?
                .ToDictionary(a => a.TopicName, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, ITopicAgent>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"TopicOrchestrator initialized with {_agentRegistry.Count} registered agents."); 

        }

        /// <summary>
        /// Processes one user turn.
        ///
        /// <para>Flow:</para>
        /// <list type="number">
        ///   <item>Records the user message in the transcript.</item>
        ///   <item>Builds an enriched prompt that prepends the recent transcript and
        ///         any verified-customer context.</item>
        ///   <item>Calls <see cref="SemanticKernelOrchestrator.SelectTopicsAsync"/> to
        ///         get an ordered topic pipeline.</item>
        ///   <item>Executes each topic in order, passing accumulated results as context
        ///         to subsequent steps.</item>
        ///   <item>Follows any <c>go_back</c> / handoff returned by a registered agent
        ///         (up to <c>MaxHandoffsPerTurn</c> hops).</item>
        ///   <item>Records the assistant reply in the transcript.</item>
        ///   <item>Returns the final response string.</item>
        /// </list>
        /// </summary>
        public async Task<string> RunTurnAsync(
            string userInput,
            CancellationToken cancellationToken = default,
            Func<string, Task>? intermediateResponseCallback = null)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return string.Empty;

            // 1 — Record user turn
            State.AddTurn("User", userInput);

            // 2 — Enrich with session context
            var enrichedInput = BuildEnrichedPrompt(userInput);

            // 3 — Route to an ordered topic pipeline
            var topics = await _kernel.SelectTopicsAsync(enrichedInput, cancellationToken).ConfigureAwait(false);

            if (topics.Count == 0)
            {
                var noMatch = "I'm not sure how to help with that. Could you rephrase?";
                State.AddTurn("Agent", noMatch);
                return noMatch;
            }

            // 4 — Execute each topic step
            var stepResults = new List<(string TopicName, string Result)>();
            int handoffCount = 0;

            for (int i = 0; i < topics.Count && handoffCount < MaxHandoffsPerTurn; i++)
            {
                var topic = topics[i];

                // Update state tracking
                State.VisitedAgents[topic.Name] = true;
                State.CurrentTopic = topic.Name;

                var fromAgent = stepResults.Count > 0
                    ? stepResults[^1].TopicName
                    : "topic_selector";

                // Build the prompt for this step
                var stepPrompt = stepResults.Count == 0
                    ? enrichedInput
                    : BuildChainedStepPrompt(userInput, stepResults);

                string stepResult;

                // 5 — Use registered ITopicAgent if available
                if (_agentRegistry.TryGetValue(topic.Name, out var agent))
                {
                    var transition = await agent.RunAsync(stepPrompt, State, cancellationToken)
                        .ConfigureAwait(false);

                    stepResult = transition.AgentResponse;

                    _transitionLog.Add(new AgentTransition
                    {
                        FromAgent      = fromAgent,
                        ToAgent        = transition.ToAgent,
                        TransitionType = transition.TransitionType,
                        TriggerAction  = transition.TriggerAction,
                        AgentResponse  = stepResult,
                        StateSnapshot  = ShallowCloneState()
                    });

                    // Follow handoff if the agent requested one
                    if (!string.IsNullOrWhiteSpace(transition.ToAgent) &&
                        !string.Equals(transition.ToAgent, "END", StringComparison.OrdinalIgnoreCase))
                    {
                        var handoffTopic = _kernel.Topics.FirstOrDefault(
                            t => string.Equals(t.Name, transition.ToAgent, StringComparison.OrdinalIgnoreCase));

                        if (handoffTopic != null)
                        {
                            topics = new List<Topic>(topics) { handoffTopic };
                            handoffCount++;
                        }
                    }
                }
                else
                {
                    // Fallback — generic kernel execution
                    stepResult = await _kernel.RunWithTopicAsync(stepPrompt, topic, cancellationToken)
                        .ConfigureAwait(false);

                    _transitionLog.Add(new AgentTransition
                    {
                        FromAgent      = fromAgent,
                        ToAgent        = i < topics.Count - 1 ? topics[i + 1].Name : null,
                        TransitionType = "handoff",
                        TriggerAction  = "auto",
                        AgentResponse  = stepResult,
                        StateSnapshot  = ShallowCloneState()
                    });
                }

                stepResults.Add((topic.Name, stepResult));
                if (intermediateResponseCallback is not null)
                {
                    await intermediateResponseCallback(stepResult).ConfigureAwait(false);
                }

                // Parse any go_back JSON embedded in the response
                var embeddedNextTopic = ParseGoBackDirective(stepResult);
                if (!string.IsNullOrEmpty(embeddedNextTopic) &&
                    !string.Equals(embeddedNextTopic, "END", StringComparison.OrdinalIgnoreCase))
                {
                    var goBackTopic = _kernel.Topics.FirstOrDefault(
                        t => string.Equals(t.Name, embeddedNextTopic, StringComparison.OrdinalIgnoreCase));

                    if (goBackTopic != null && !topics.Contains(goBackTopic))
                    {
                        topics = new List<Topic>(topics) { goBackTopic };
                        handoffCount++;
                    }
                }
            }

            // 6 — Synthesise final answer when more than one step ran
            string finalResponse;
            if (stepResults.Count == 1)
            {
                finalResponse = stepResults[0].Result;
            }
            else
            {
                finalResponse = BuildCombinedResponse(stepResults);
            }

            // 7 — Record assistant reply
            State.AddTurn("Agent", finalResponse);
            if (intermediateResponseCallback is not null)
            {
                await intermediateResponseCallback(finalResponse).ConfigureAwait(false);
            }
            return finalResponse;
        }

        // ── Prompt helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Prepends the recent conversation transcript and customer context header
        /// to the raw user input so every LLM call has full session context.
        /// </summary>
        private string BuildEnrichedPrompt(string userInput)
        {
            var sb = new StringBuilder();

            var customerHeader = State.GetCustomerContextHeader();
            if (!string.IsNullOrEmpty(customerHeader))
            {
                sb.AppendLine(customerHeader);
                sb.AppendLine();
            }

            var transcript = State.GetRecentTranscript(TranscriptWindowTurns);
            if (!string.IsNullOrEmpty(transcript))
            {
                sb.AppendLine("--- Recent conversation ---");
                sb.AppendLine(transcript);
                sb.AppendLine("--- End of recent conversation ---");
                sb.AppendLine();
            }

            sb.Append(userInput);
            return sb.ToString();
        }

        /// <summary>
        /// Builds the prompt for step N of a multi-topic chain, injecting the
        /// results gathered by earlier steps.
        /// </summary>
        private static string BuildChainedStepPrompt(
            string originalUserInput,
            IReadOnlyList<(string TopicName, string Result)> priorResults)
        {
            var sb = new StringBuilder();
            sb.AppendLine(originalUserInput);
            sb.AppendLine();
            sb.AppendLine("--- Context from earlier steps ---");
            foreach (var (name, result) in priorResults)
                sb.AppendLine($"[{name}]: {result}");
            sb.AppendLine("--- Use the above context to complete your part of the task ---");
            return sb.ToString();
        }

        /// <summary>
        /// Concatenates step results into one readable response (fallback when no
        /// LLM synthesis is needed — e.g. two short declarative answers).
        /// </summary>
        private static string BuildCombinedResponse(
            IReadOnlyList<(string TopicName, string Result)> stepResults)
        {
            var sb = new StringBuilder();
            foreach (var (_, result) in stepResults)
            {
                sb.AppendLine(result);
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        // ── go_back parsing ───────────────────────────────────────────────────

        private static readonly Regex GoBackPattern =
            new(@"\{""next_topic""\s*:\s*""(?<topic>[^""]+)""\}", RegexOptions.Compiled);

        /// <summary>
        /// Extracts the value of <c>next_topic</c> from a <c>{"next_topic": "..."}</c>
        /// fragment that the LLM may embed in its response when it calls <c>go_back</c>.
        /// Returns <c>null</c> if no such fragment is present.
        /// </summary>
        private static string? ParseGoBackDirective(string response)
        {
            var match = GoBackPattern.Match(response ?? string.Empty);
            return match.Success ? match.Groups["topic"].Value : null;
        }

        // ── State helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates a shallow snapshot of the current <see cref="ConversationState"/>
        /// for the transition audit log.  The snapshot holds primitive values only —
        /// the transcript list is intentionally not copied.
        /// </summary>
        private ConversationState ShallowCloneState() => new()
        {
            SessionId          = State.SessionId,
            PlanId             = State.PlanId,
            CurrentTopic       = State.CurrentTopic,
            Locale             = State.Locale,
            VisitedAgents      = new Dictionary<string, bool>(State.VisitedAgents),
            CustomerVerified   = State.CustomerVerified,
            CustomerId         = State.CustomerId,
            CustomerName       = State.CustomerName,
            CustomerEmail      = State.CustomerEmail,
            PhoneNumber        = State.PhoneNumber,
            AccountNumber      = State.AccountNumber,
            ServicePlan        = State.ServicePlan,
            AddressOnFile      = State.AddressOnFile,
            BillingBalance     = State.BillingBalance,
            BillingDueDate     = State.BillingDueDate,
            BillingDisputeId   = State.BillingDisputeId,
            BillingDisputeStatus = State.BillingDisputeStatus,
            TicketId           = State.TicketId,
            TicketStatus       = State.TicketStatus,
            InternetSpeedTier  = State.InternetSpeedTier,
            ModemStatus        = State.ModemStatus,
            DiagnosticResults  = State.DiagnosticResults,
            PlanName           = State.PlanName,
            PlanPrice          = State.PlanPrice,
            UpgradeEligible    = State.UpgradeEligible,
            PromoCode          = State.PromoCode,
        };
    }
}

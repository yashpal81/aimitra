using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aimitra.Core.Models;

namespace Aimitra.Services.Tracing
{
    // ── JSON models (match the AimitraLens trace format exactly) ─────────────

    public sealed class AimitraTrace
    {
        [JsonPropertyName("traceId")]
        public string TraceId { get; init; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("timestamp")]
        public string Timestamp { get; init; } = DateTime.UtcNow.ToString("O");

        [JsonPropertyName("summary")]
        public TraceSummary Summary { get; init; } = new();

        [JsonPropertyName("graph")]
        public TraceGraph Graph { get; init; } = new();

        [JsonPropertyName("timeline")]
        public List<TimelineStep> Timeline { get; init; } = new();
    }

    public sealed class TraceSummary
    {
        [JsonPropertyName("totalDurationMs")]
        public long TotalDurationMs { get; set; }

        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; set; }

        [JsonPropertyName("agentsInvolved")]
        public List<string> AgentsInvolved { get; set; } = new();
    }

    public sealed class TraceGraph
    {
        [JsonPropertyName("nodes")]
        public List<GraphNode> Nodes { get; set; } = new();

        [JsonPropertyName("edges")]
        public List<GraphEdge> Edges { get; set; } = new();
    }

    public sealed class GraphNode
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = "agent";   // agent | tool

        [JsonPropertyName("label")]
        public string Label { get; init; } = string.Empty;
    }

    public sealed class GraphEdge
    {
        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; init; } = string.Empty;

        [JsonPropertyName("order")]
        public int Order { get; init; }

        [JsonPropertyName("type")]
        public string Type { get; init; } = "handoff";  // handoff | tool_call | escalate | fallback
    }

    public sealed class TimelineStep
    {
        [JsonPropertyName("step")]
        public int Step { get; init; }

        [JsonPropertyName("spanId")]
        public string SpanId { get; init; } = Guid.NewGuid().ToString("N")[..16];

        [JsonPropertyName("parentSpanId")]
        public string ParentSpanId { get; init; } = "0000000000000000";

        [JsonPropertyName("component")]
        public string Component { get; init; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;   // LLM_CALL | TOOL_EXECUTION | HANDOFF

        [JsonPropertyName("status")]
        public string Status { get; init; } = "Success";

        [JsonPropertyName("durationMs")]
        public long DurationMs { get; init; }

        [JsonPropertyName("inspector")]
        public StepInspector? Inspector { get; init; }

        [JsonPropertyName("fsmState")]
        public FsmState? FsmState { get; init; }
    }

    public sealed class StepInspector
    {
        // LLM call fields
        [JsonPropertyName("systemPrompt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SystemPrompt { get; init; }

        [JsonPropertyName("userMessage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserMessage { get; init; }

        [JsonPropertyName("modelOutput")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ModelOutput { get; init; }

        [JsonPropertyName("usage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TokenUsage? Usage { get; init; }

        // Tool call fields
        [JsonPropertyName("toolName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolName { get; init; }

        [JsonPropertyName("toolInput")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolInput { get; init; }

        [JsonPropertyName("toolOutput")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolOutput { get; init; }
    }

    public sealed class TokenUsage
    {
        [JsonPropertyName("promptTokens")]
        public int PromptTokens { get; init; }

        [JsonPropertyName("completionTokens")]
        public int CompletionTokens { get; init; }

        [JsonPropertyName("totalTokens")]
        public int TotalTokens { get; init; }
    }

    public sealed class FsmState
    {
        [JsonPropertyName("currentState")]
        public string CurrentState { get; init; } = string.Empty;

        [JsonPropertyName("variableDiff")]
        public Dictionary<string, object?> VariableDiff { get; init; } = new();
    }

    // ── TraceBuilder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the <see cref="AgentTransition"/> log produced by
    /// <c>TopicOrchestrator</c> into an <see cref="AimitraTrace"/> JSON document
    /// that can be dropped straight into AimitraLens for visual debugging.
    ///
    /// Usage:
    /// <code>
    /// var trace = TraceBuilder.Build(orchestrator.TransitionLog, orchestrator.State);
    /// File.WriteAllText("trace.json", TraceBuilder.ToJson(trace));
    /// </code>
    /// </summary>
    public static class TraceBuilder
    {
        /// <summary>
        /// Builds a complete <see cref="AimitraTrace"/> from the transition log
        /// accumulated by a <c>TopicOrchestrator</c> session.
        /// </summary>
        public static AimitraTrace Build(
            IReadOnlyList<AgentTransition> transitions,
            ConversationState state,
            string? traceId = null)
        {
            var nodeIds   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nodes     = new List<GraphNode>();
            var edges     = new List<GraphEdge>();
            var timeline  = new List<TimelineStep>();
            var spanIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long totalMs  = 0;
            int edgeOrder = 0;

            void EnsureNode(string id, string type)
            {
                if (nodeIds.Add(id))
                    nodes.Add(new GraphNode { Id = id, Type = type, Label = FormatLabel(id) });
            }

            for (int i = 0; i < transitions.Count; i++)
            {
                var t     = transitions[i];
                var spanId = Guid.NewGuid().ToString("N")[..16];
                var parentSpan = i > 0 && spanIndex.TryGetValue(transitions[i - 1].FromAgent, out var ps)
                    ? ps : "0000000000000000";

                spanIndex[t.FromAgent] = spanId;

                // Graph nodes
                EnsureNode(t.FromAgent, "agent");
                if (!string.IsNullOrEmpty(t.ToAgent) &&
                    !string.Equals(t.ToAgent, "END", StringComparison.OrdinalIgnoreCase))
                    EnsureNode(t.ToAgent, "agent");

                // Graph edge
                if (!string.IsNullOrEmpty(t.ToAgent) &&
                    !string.Equals(t.ToAgent, "END", StringComparison.OrdinalIgnoreCase))
                {
                    edges.Add(new GraphEdge
                    {
                        Source = t.FromAgent,
                        Target = t.ToAgent,
                        Order  = ++edgeOrder,
                        Type   = t.TransitionType ?? "handoff"
                    });
                }

                // Build variable diff from state snapshot
                var varDiff = BuildVariableDiff(t.StateSnapshot, i == 0 ? null : transitions[i - 1].StateSnapshot);

                // Timeline step — agent turn
                var step = new TimelineStep
                {
                    Step       = i + 1,
                    SpanId     = spanId,
                    ParentSpanId = parentSpan,
                    Component  = t.FromAgent,
                    Type       = "LLM_CALL",
                    Status     = "Success",
                    DurationMs = 0,    // populated by timed wrapper — see TimedTraceBuilder
                    Inspector  = new StepInspector
                    {
                        ModelOutput = string.IsNullOrWhiteSpace(t.AgentResponse)
                            ? null : t.AgentResponse
                    },
                    FsmState = new FsmState
                    {
                        CurrentState = MapFsmState(t.TransitionType, t.TriggerAction),
                        VariableDiff = varDiff
                    }
                };
                timeline.Add(step);

                // If this was a tool call, add a child step for the trigger action
                if (!string.IsNullOrWhiteSpace(t.TriggerAction) &&
                    t.TriggerAction != "auto" &&
                    t.TriggerAction != "guardrail" &&
                    t.TriggerAction != "pending_verification")
                {
                    var toolSpan = Guid.NewGuid().ToString("N")[..16];
                    EnsureNode(t.TriggerAction, "tool");
                    edges.Add(new GraphEdge
                    {
                        Source = t.FromAgent,
                        Target = t.TriggerAction,
                        Order  = ++edgeOrder,
                        Type   = "tool_call"
                    });

                    timeline.Add(new TimelineStep
                    {
                        Step         = timeline.Count + 1,
                        SpanId       = toolSpan,
                        ParentSpanId = spanId,
                        Component    = t.TriggerAction,
                        Type         = "TOOL_EXECUTION",
                        Status       = "Success",
                        DurationMs   = 0,
                        Inspector    = new StepInspector
                        {
                            ToolName   = t.TriggerAction,
                            ToolOutput = ExtractToolOutputHint(t)
                        },
                        FsmState = new FsmState
                        {
                            CurrentState = "ToolExecution",
                            VariableDiff = varDiff
                        }
                    });
                }
            }

            var summary = new TraceSummary
            {
                TotalDurationMs  = totalMs,
                TotalTokens      = 0,   // populated when real token data is wired in
                AgentsInvolved   = nodes.Where(n => n.Type == "agent").Select(n => n.Id).ToList()
            };

            return new AimitraTrace
            {
                TraceId   = traceId ?? Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow.ToString("O"),
                Summary   = summary,
                Graph     = new TraceGraph { Nodes = nodes, Edges = edges },
                Timeline  = timeline
            };
        }

        /// <summary>Serialises a trace to indented JSON.</summary>
        public static string ToJson(AimitraTrace trace) =>
            JsonSerializer.Serialize(trace, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FormatLabel(string id) =>
            // "topic_selector" → "Topic Selector Agent"
            string.Join(" ", id.Split('_')
                .Select(w => char.ToUpper(w[0]) + w[1..]));

        private static string MapFsmState(string transitionType, string triggerAction) =>
            triggerAction switch
            {
                "verify_customer"       => "CustomerVerification",
                "billing_inquiry"       => "BillingLookup",
                "file_billing_dispute"  => "DisputeFiling",
                "run_network_diagnostics" => "NetworkDiagnostics",
                "create_support_ticket" => "TicketCreation",
                "schedule_technician"   => "TechnicianScheduling",
                "go_back"               => "TopicHandoff",
                "guardrail"             => "GuardrailBlock",
                _ => transitionType switch
                {
                    "handoff"   => "RoutingDecision",
                    "escalate"  => "EscalationDecision",
                    "fallback"  => "FallbackResponse",
                    "complete"  => "SessionComplete",
                    _           => "AgentExecution"
                }
            };

        private static string? ExtractToolOutputHint(AgentTransition t)
        {
            // When a plugin sets state, surface the relevant state fields as the "tool output"
            var snap = t.StateSnapshot;
            if (snap == null) return null;

            return t.TriggerAction switch
            {
                "verify_customer" => snap.CustomerVerified
                    ? $"{{ \"success\": true, \"customerId\": \"{snap.CustomerId}\", \"customerName\": \"{snap.CustomerName}\" }}"
                    : "{ \"success\": false }",

                "file_billing_dispute" => string.IsNullOrEmpty(snap.BillingDisputeId)
                    ? null
                    : $"{{ \"disputeId\": \"{snap.BillingDisputeId}\", \"status\": \"{snap.BillingDisputeStatus}\" }}",

                "create_support_ticket" => string.IsNullOrEmpty(snap.TicketId)
                    ? null
                    : $"{{ \"ticketId\": \"{snap.TicketId}\", \"status\": \"{snap.TicketStatus}\" }}",

                _ => null
            };
        }

        private static Dictionary<string, object?> BuildVariableDiff(
            ConversationState? current,
            ConversationState? previous)
        {
            if (current == null) return new();

            var diff = new Dictionary<string, object?>();

            if (previous == null || current.CurrentTopic != previous.CurrentTopic)
                diff["currentTopic"] = current.CurrentTopic;

            if (previous == null || current.CustomerVerified != previous.CustomerVerified)
                diff["customerVerified"] = current.CustomerVerified;

            if (!string.IsNullOrEmpty(current.CustomerId) &&
                (previous == null || current.CustomerId != previous.CustomerId))
                diff["customerId"] = current.CustomerId;

            if (!string.IsNullOrEmpty(current.CustomerName) &&
                (previous == null || current.CustomerName != previous.CustomerName))
                diff["customerName"] = current.CustomerName;

            if (!string.IsNullOrEmpty(current.TicketId) &&
                (previous == null || current.TicketId != previous.TicketId))
                diff["ticketId"] = current.TicketId;

            if (!string.IsNullOrEmpty(current.BillingDisputeId) &&
                (previous == null || current.BillingDisputeId != previous.BillingDisputeId))
                diff["billingDisputeId"] = current.BillingDisputeId;

            if (current.BillingBalance != 0 &&
                (previous == null || current.BillingBalance != previous.BillingBalance))
                diff["billingBalance"] = current.BillingBalance;

            return diff;
        }
    }
}

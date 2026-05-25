namespace Aimitra.Core.Models
{
    /// <summary>
    /// Describes a single handoff between two agents.
    /// The <see cref="TopicOrchestrator"/> records one instance for every hop
    /// so the full routing chain can be audited.
    /// </summary>
    public class AgentTransition
    {
        /// <summary>Agent (topic name) that produced this transition.</summary>
        public string FromAgent { get; set; } = string.Empty;

        /// <summary>
        /// Agent (topic name) to route to next.
        /// <c>null</c> or <c>"END"</c> signals the conversation has finished.
        /// </summary>
        public string? ToAgent { get; set; }

        /// <summary>
        /// Classification of the transition.
        /// Common values: <c>handoff</c>, <c>escalate</c>, <c>fallback</c>.
        /// </summary>
        public string TransitionType { get; set; } = "handoff";

        /// <summary>
        /// The tool / action name that triggered this transition
        /// (e.g. <c>go_back</c>, <c>billing_inquiry</c>).
        /// </summary>
        public string TriggerAction { get; set; } = string.Empty;

        /// <summary>The agent's textual response to the user for this hop.</summary>
        public string AgentResponse { get; set; } = string.Empty;

        /// <summary>
        /// Shallow snapshot of <see cref="ConversationState"/> at the moment of the transition.
        /// Stored for audit; the live state object continues to be mutated.
        /// </summary>
        public ConversationState? StateSnapshot { get; set; }
    }
}

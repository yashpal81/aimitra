using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Aimitra.Services.Plugins
{
    /// <summary>
    /// Universal navigation tool registered in every scoped agent kernel.
    ///
    /// When the LLM decides the current agent cannot fully serve the user's request,
    /// it calls <see cref="GoBack"/> to hand control back to the topic selector or
    /// directly to a named agent. The <see cref="TopicOrchestrator"/> reads
    /// <see cref="ConsumeNextTopic"/> after each LLM turn and follows the directive.
    ///
    /// Mirrors the <c>go_back</c> tool seen in every agent in the Apex Telecom trace.
    /// </summary>
    public sealed class NavigationPlugin
    {
        private string? _pendingNextTopic;

        /// <summary>
        /// Routes the conversation to a different topic or back to the topic selector.
        /// </summary>
        /// <param name="nextTopic">
        /// The topic to route to. Use one of:
        /// <c>topic_selector</c> | <c>verification</c> | <c>billing</c> |
        /// <c>technical_support</c> | <c>plan_management</c> | <c>account_settings</c>
        /// </param>
        [KernelFunction("go_back")]
        [Description(
            "Routes the conversation to a different topic or back to the topic selector. " +
            "Call this when the user's request is outside the current topic's scope, " +
            "or when this agent has finished its part and another agent should continue.")]
        public string GoBack(
            [Description(
                "Target topic: topic_selector | verification | billing | " +
                "technical_support | plan_management | account_settings")]
            string nextTopic)
        {
            _pendingNextTopic = nextTopic?.Trim();
            return $"{{\"next_topic\": \"{_pendingNextTopic}\"}}";
        }

        /// <summary>
        /// Returns the topic name requested by the last <see cref="GoBack"/> call and
        /// resets the pending value. Returns <c>null</c> if <see cref="GoBack"/> was
        /// not called during this LLM turn.
        /// </summary>
        public string? ConsumeNextTopic()
        {
            var value = _pendingNextTopic;
            _pendingNextTopic = null;
            return value;
        }

        /// <summary>
        /// <c>true</c> when the LLM has issued a <see cref="GoBack"/> call that has
        /// not yet been consumed by the orchestrator.
        /// </summary>
        public bool HasPendingNavigation => _pendingNextTopic is not null;
    }
}

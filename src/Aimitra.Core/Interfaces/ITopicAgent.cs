using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;

namespace Aimitra.Core.Interfaces
{
    /// <summary>
    /// Contract for every domain agent in the Aimitra topic-action framework.
    ///
    /// Each implementation handles one <see cref="TopicName"/>, runs its scoped
    /// Semantic Kernel, and returns an <see cref="AgentTransition"/> that tells
    /// <c>TopicOrchestrator</c> where to route next (or to end the conversation).
    ///
    /// Agents mutate the shared <see cref="ConversationState"/> directly so that
    /// facts discovered in one turn (e.g. a customer ID after verification) are
    /// automatically available to every subsequent agent hop.
    /// </summary>
    public interface ITopicAgent
    {
        /// <summary>
        /// Unique name that matches a <c>Topic.Name</c> used by the router
        /// (e.g. <c>"verification"</c>, <c>"billing"</c>, <c>"technical_support"</c>).
        /// </summary>
        string TopicName { get; }

        /// <summary>
        /// Human-readable description used as the routing function description
        /// when this agent is registered as a topic in the router.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Executes one agent turn.
        /// </summary>
        /// <param name="userInput">
        /// The user message for this turn (may be enriched with prior context
        /// by <c>TopicOrchestrator</c> before being passed here).
        /// </param>
        /// <param name="state">
        /// The shared mutable session state. The agent reads facts it needs
        /// (e.g. <c>state.CustomerId</c>) and writes facts it produces
        /// (e.g. <c>state.CustomerVerified = true</c>).
        /// </param>
        /// <param name="cancellationToken">Propagated cancellation token.</param>
        /// <returns>
        /// An <see cref="AgentTransition"/> describing what the agent did and
        /// where the conversation should go next.
        /// Set <see cref="AgentTransition.ToAgent"/> to <c>null</c> or <c>"END"</c>
        /// to terminate the session; set it to another topic name to hand off.
        /// </returns>
        Task<AgentTransition> RunAsync(
            string userInput,
            ConversationState state,
            CancellationToken cancellationToken = default);
    }
}

using Aimitra.Core.Models;
using Aimitra.Security.Guardrails;

namespace Aimitra.Services.Orchestration
{
    /// <summary>
    /// Wraps every final agent turn response, attaching safety and grounding metadata.
    ///
    /// Returned by <c>TopicOrchestrator.RunTurnAsync()</c> so the caller (Console, API
    /// controller, test harness) can inspect the verdict without re-running any checks.
    ///
    /// Mirrors the <c>PlannerResponseStep</c> shape in the Apex Telecom trace:
    /// <code>
    /// {
    ///   "message": "...",
    ///   "responseType": "Inform",
    ///   "isContentSafe": true,
    ///   "safetyScore": { ... },
    ///   "reasoning": { "category": "GROUNDED", "reason": "..." }
    /// }
    /// </code>
    /// </summary>
    public sealed class PlannerResponse
    {
        /// <summary>The final user-facing text produced by the agent.</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>
        /// Classifies the response intent.
        /// <list type="bullet">
        ///   <item><c>Inform</c>     — sharing information the customer asked for.</item>
        ///   <item><c>RequestInfo</c> — asking the customer for more details.</item>
        ///   <item><c>Confirm</c>    — confirming a completed action.</item>
        ///   <item><c>Error</c>      — reporting a failure or guardrail block.</item>
        /// </list>
        /// </summary>
        public string ResponseType { get; init; } = "Inform";

        /// <summary>
        /// <c>true</c> when neither the InappropriateContent nor the PromptInjection
        /// guardrail triggered during this turn.
        /// </summary>
        public bool IsContentSafe { get; init; } = true;

        /// <summary>
        /// Guardrail evaluation detail.  <see cref="GuardrailResult.IsSafe"/> matches
        /// <see cref="IsContentSafe"/>.  <c>null</c> when no guardrail was evaluated
        /// (e.g. the turn was blocked before reaching the LLM).
        /// </summary>
        public GuardrailResult? SafetyScore { get; init; }

        /// <summary>
        /// Grounding verdict for <see cref="Message"/>.
        /// <see cref="ReasoningValidation.Category"/> is one of
        /// <c>GROUNDED</c>, <c>HALLUCINATED</c>, or <c>UNVERIFIABLE</c>.
        /// </summary>
        public ReasoningValidation Reasoning { get; init; } =
            ReasoningValidation.Unverifiable("Reasoning check not yet run.");

        // ── Factory helpers ───────────────────────────────────────────────────

        /// <summary>Creates a safe, grounded response.</summary>
        public static PlannerResponse Success(
            string message,
            string responseType,
            GuardrailResult safetyScore,
            ReasoningValidation reasoning) =>
            new()
            {
                Message       = message,
                ResponseType  = responseType,
                IsContentSafe = true,
                SafetyScore   = safetyScore,
                Reasoning     = reasoning
            };

        /// <summary>Creates a response that was blocked by a guardrail.</summary>
        public static PlannerResponse Blocked(string reason, GuardrailResult safetyScore) =>
            new()
            {
                Message       = reason,
                ResponseType  = "Error",
                IsContentSafe = false,
                SafetyScore   = safetyScore,
                Reasoning     = ReasoningValidation.Unverifiable("Turn was blocked before LLM execution.")
            };
    }
}

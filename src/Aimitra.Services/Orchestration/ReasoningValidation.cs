using System.Collections.Generic;

namespace Aimitra.Core.Models
{
    /// <summary>
    /// The outcome of a <c>ReasoningValidator</c> grounding check.
    ///
    /// After the LLM produces a response the validator compares every verifiable
    /// claim in that response (currency amounts, dates, IDs, speeds, etc.) against
    /// the tool outputs collected in the same agent turn.
    ///
    /// <list type="bullet">
    ///   <item><b>GROUNDED</b> — every extracted claim was found in at least one tool output.</item>
    ///   <item><b>HALLUCINATED</b> — one or more claims could not be traced to any tool output.</item>
    ///   <item><b>UNVERIFIABLE</b> — the response contained no extractable verifiable claims,
    ///         or no tool outputs were available to compare against.</item>
    /// </list>
    /// </summary>
    public sealed class ReasoningValidation
    {
        /// <summary>
        /// Verdict: <c>GROUNDED</c>, <c>HALLUCINATED</c>, or <c>UNVERIFIABLE</c>.
        /// </summary>
        public string Category { get; init; } = "UNVERIFIABLE";

        /// <summary>
        /// Human-readable explanation of the verdict, suitable for logging and UI display.
        /// </summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// Claims (token values) that were found in at least one tool output.
        /// </summary>
        public IReadOnlyList<string> GroundedClaims { get; init; } = System.Array.Empty<string>();

        /// <summary>
        /// Claims that were present in the LLM response but absent from all tool outputs.
        /// Non-empty only when <see cref="Category"/> is <c>HALLUCINATED</c>.
        /// </summary>
        public IReadOnlyList<string> UngroundedClaims { get; init; } = System.Array.Empty<string>();

        /// <summary>Shorthand: <c>true</c> when Category is <c>GROUNDED</c>.</summary>
        public bool IsGrounded => Category == "GROUNDED";

        // ── Factory helpers ───────────────────────────────────────────────────

        internal static ReasoningValidation Grounded(IReadOnlyList<string> grounded) =>
            new()
            {
                Category       = "GROUNDED",
                Reason         = $"All {grounded.Count} verifiable claim(s) confirmed in tool outputs.",
                GroundedClaims = grounded
            };

        internal static ReasoningValidation Hallucinated(
            IReadOnlyList<string> grounded,
            IReadOnlyList<string> ungrounded) =>
            new()
            {
                Category         = "HALLUCINATED",
                Reason           = $"{ungrounded.Count} of {grounded.Count + ungrounded.Count} verifiable " +
                                   $"claim(s) not found in tool outputs: {string.Join(", ", ungrounded)}",
                GroundedClaims   = grounded,
                UngroundedClaims = ungrounded
            };

        internal static ReasoningValidation Unverifiable(string reason) =>
            new() { Category = "UNVERIFIABLE", Reason = reason };
    }
}

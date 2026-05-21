using System.Collections.Generic;

namespace Aimitra.Security.Guardrails
{
    /// <summary>
    /// Represents the outcome of a guardrail evaluation.
    /// Mirrors the safetyScore shape used in the PlannerResponseStep of the trace.
    /// </summary>
    public sealed class GuardrailResult
    {
        public bool IsSafe { get; init; }

        /// <summary>
        /// Short label for the category that triggered the violation, e.g. "toxicity", "prompt_injection".
        /// Empty when IsSafe is true.
        /// </summary>
        public string ViolationType { get; init; } = string.Empty;

        /// <summary>Human-readable reason returned to the agent or logged for audit.</summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>Overall risk score: 0.0 = clean, 1.0 = definite violation.</summary>
        public double Score { get; init; }

        /// <summary>Per-category breakdown, matching the trace safetyScore shape.</summary>
        public IReadOnlyDictionary<string, double> CategoryScores { get; init; }
            = new Dictionary<string, double>();

        public static GuardrailResult Safe() => new() { IsSafe = true, Score = 0.0 };

        public static GuardrailResult Violation(string violationType, string reason, double score,
            IReadOnlyDictionary<string, double> categoryScores)
            => new()
            {
                IsSafe = false,
                ViolationType = violationType,
                Reason = reason,
                Score = score,
                CategoryScores = categoryScores
            };
    }

    /// <summary>
    /// Thrown by guardrail filters when a violation is detected in the Semantic Kernel pipeline.
    /// Catching this exception in the orchestrator allows graceful degradation instead of a crash.
    /// </summary>
    public sealed class GuardrailViolationException : System.Exception
    {
        public GuardrailResult Result { get; }

        public GuardrailViolationException(GuardrailResult result)
            : base($"Guardrail violation [{result.ViolationType}]: {result.Reason}")
        {
            Result = result;
        }
    }
}

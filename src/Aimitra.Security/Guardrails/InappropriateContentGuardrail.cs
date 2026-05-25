using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace Aimitra.Security.Guardrails
{
    /// <summary>
    /// Inappropriate Content Guardrail.
    ///
    /// Dual-mode design (mirrors the trace architecture):
    ///   1. IPromptRenderFilter  — automatically screens every prompt before it reaches the LLM.
    ///   2. KernelFunction       — the LLM can call CheckContent() explicitly as a tool,
    ///                             which is how it appears in EnabledToolsStep / tools_sent in the trace.
    ///
    /// Categories evaluated (matching the trace safetyScore shape):
    ///   toxicity | hate | identity | violence | physical | sexual | profanity | biased
    /// </summary>
    public sealed class InappropriateContentGuardrail : IPromptRenderFilter, IAutoFunctionInvocationFilter
    {
        // Score threshold above which the content is blocked.
        private readonly double _blockThreshold;

        // Each entry: (category, regex-pattern, weight)
        private static readonly IReadOnlyList<(string Category, Regex Pattern, double Weight)> Rules
            = BuildRules();

        public InappropriateContentGuardrail(double blockThreshold = 0.5)
        {
            _blockThreshold = blockThreshold;
        }

        // ----------------------------------------------------------------
        // KernelFunction — callable by the LLM as an explicit tool
        // ----------------------------------------------------------------

        /// <summary>
        /// Evaluates text for inappropriate or harmful content.
        /// Returns a JSON-style summary so the LLM can decide how to respond.
        /// </summary>
        [KernelFunction("Inappropriate_Content")]
        [Description("Checks user input or any text for inappropriate, harmful, or policy-violating content. " +
                     "Returns a safety assessment with category scores. Call this before processing sensitive requests.")]
        public string CheckContent(
            [Description("The text to evaluate.")] string text)
        {
            var result = Evaluate(text);
            if (result.IsSafe)
                return "{\"safe\": true, \"score\": 0.0}";

            return $"{{\"safe\": false, \"violation\": \"{result.ViolationType}\", " +
                   $"\"score\": {result.Score:F2}, \"reason\": \"{EscapeJson(result.Reason)}\"}}";
        }

        // ----------------------------------------------------------------
        // IPromptRenderFilter — screens every prompt before it reaches the LLM
        // ----------------------------------------------------------------

        public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
        {
            if (!string.IsNullOrWhiteSpace(context.RenderedPrompt))
            {
                var result = Evaluate(context.RenderedPrompt);
                if (!result.IsSafe)
                {
                    Console.WriteLine(
                        $"[InappropriateContentGuardrail] Prompt blocked — {result.ViolationType}: {result.Reason}");
                    throw new GuardrailViolationException(result);
                }
            }
            await next(context);
        }

        // ----------------------------------------------------------------
        // IAutoFunctionInvocationFilter — screens every function call argument
        // ----------------------------------------------------------------

        public async Task OnAutoFunctionInvocationAsync(
            AutoFunctionInvocationContext context,
            Func<AutoFunctionInvocationContext, Task> next)
        {
            foreach (var name in context.Arguments.Names)
            {
                var value = context.Arguments[name]?.ToString();
                if (string.IsNullOrWhiteSpace(value)) continue;

                var result = Evaluate(value);
                if (!result.IsSafe)
                {
                    Console.WriteLine(
                        $"[InappropriateContentGuardrail] Function arg '{name}' blocked — {result.ViolationType}: {result.Reason}");
                    throw new GuardrailViolationException(result);
                }
            }
            await next(context);
        }

        // ----------------------------------------------------------------
        // Core evaluation logic
        // ----------------------------------------------------------------

        public GuardrailResult Evaluate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return GuardrailResult.Safe();

            var scores = new Dictionary<string, double>
            {
                ["toxicity"]  = 0.0,
                ["hate"]      = 0.0,
                ["identity"]  = 0.0,
                ["violence"]  = 0.0,
                ["physical"]  = 0.0,
                ["sexual"]    = 0.0,
                ["profanity"] = 0.0,
                ["biased"]    = 0.0,
            };

            string lowerText = text.ToLowerInvariant();
            string topCategory = string.Empty;
            double topScore = 0.0;

            foreach (var (category, pattern, weight) in Rules)
            {
                var matches = pattern.Matches(lowerText);
                if (matches.Count == 0) continue;

                // Score increases with number of matches, capped at 1.0
                double raw = Math.Min(1.0, matches.Count * weight);
                if (raw > scores[category])
                    scores[category] = raw;

                if (raw > topScore)
                {
                    topScore = raw;
                    topCategory = category;
                }
            }

            if (topScore >= _blockThreshold)
            {
                return GuardrailResult.Violation(
                    violationType: topCategory,
                    reason: $"Content scored {topScore:F2} on '{topCategory}' (threshold {_blockThreshold:F2}).",
                    score: topScore,
                    categoryScores: scores);
            }

            return GuardrailResult.Safe();
        }

        // ----------------------------------------------------------------
        // Rule definitions
        // ----------------------------------------------------------------

        private static IReadOnlyList<(string, Regex, double)> BuildRules()
        {
            return new List<(string, Regex, double)>
            {
                // Toxicity — general aggressive/abusive language
                ("toxicity", Compile(@"\b(idiot|moron|loser|stupid|dumb|fool|jerk|scum|trash|garbage)\b"), 0.4),
                ("toxicity", Compile(@"\b(shut up|go to hell|drop dead|get lost)\b"), 0.5),

                // Hate speech — group-based derogatory language
                ("hate", Compile(@"\b(nazi|fascist|supremacist)\b"), 0.7),
                ("hate", Compile(@"\b(all \w+ should|kill all|exterminate)\b"), 0.9),

                // Identity-based attacks — protected characteristics
                ("identity", Compile(@"\b(racial slur|homophobic|transphobic|islamophobic|antisemitic)\b"), 0.8),
                ("identity", Compile(@"\b(go back to your country|not your kind)\b"), 0.7),

                // Violence — explicit threats or descriptions
                ("violence", Compile(@"\b(i (will |want to |am going to )(kill|murder|attack|hurt|shoot|stab))\b"), 0.9),
                ("violence", Compile(@"\b(bomb|explosive|weapon|firearm|gun)\b"), 0.5),
                ("violence", Compile(@"\b(how to (make|build|create) (a bomb|explosives|weapon))\b"), 1.0),

                // Physical threat — directed at a person
                ("physical", Compile(@"\b(i (will |am going to )?(hurt|beat|harm|injure) (you|them|him|her))\b"), 0.9),
                ("physical", Compile(@"\b(watch your back|you('ll| will) regret|threatening)\b"), 0.6),

                // Sexual — explicit adult content
                ("sexual", Compile(@"\b(explicit sexual content placeholder|pornographic|obscene)\b"), 0.9),

                // Profanity — strong offensive words (pattern is illustrative, expand as needed)
                ("profanity", Compile(@"\b(f\*ck|sh[i1]t|a[s$]{2}hole|b[i1]tch)\b"), 0.5),
                ("profanity", Compile(@"\b(wtf|stfu|gtfo)\b"), 0.3),

                // Biased — manipulative or discriminatory framing
                ("biased", Compile(@"\b(all \w+ are (criminals|terrorists|liars))\b"), 0.8),
                ("biased", Compile(@"\b(women (can't|cannot|shouldn't)|men always)\b"), 0.6),
            };
        }

        private static Regex Compile(string pattern)
            => new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}

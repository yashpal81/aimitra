using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aimitra.Core.Models;

namespace Aimitra.Services.Orchestration
{
    /// <summary>
    /// Verifies that an LLM response is grounded in the tool outputs produced during
    /// the same agent turn.  Mirrors the <c>PlannerResponseStep</c> GROUNDED / HALLUCINATED
    /// verdict seen in the Apex Telecom trace.
    ///
    /// <para><b>Algorithm</b></para>
    /// <list type="number">
    ///   <item>Extract every <i>verifiable claim</i> from the LLM response using a set of
    ///         domain-aware regex patterns (currency amounts, ISO dates, natural-language dates,
    ///         structured IDs, data-transfer speeds, latency values, phone numbers,
    ///         and percentages).</item>
    ///   <item>For each extracted claim, check whether it appears verbatim (case-insensitive)
    ///         in the concatenated tool outputs.</item>
    ///   <item>If every claim is found → <c>GROUNDED</c>.</item>
    ///   <item>If any claim is missing → <c>HALLUCINATED</c>, with the offending claims listed.</item>
    ///   <item>If no extractable claims exist, or no tool outputs were supplied →
    ///         <c>UNVERIFIABLE</c>.</item>
    /// </list>
    ///
    /// <para>This class is stateless and thread-safe; all regex instances are compiled once
    /// and shared via static fields.</para>
    /// </summary>
    public sealed class ReasoningValidator
    {
        // ── Claim-extraction patterns ─────────────────────────────────────────
        //
        // Only concrete, falsifiable tokens are extracted — adjectives and vague
        // statements are intentionally ignored.  The goal is to catch numeric /
        // identifier hallucinations, not to flag hedged language.

        /// <summary>Currency amounts, e.g. <c>$123.45</c>, <c>$1,200</c>.</summary>
        private static readonly Regex _currency = new(
            @"\$[\d,]+(?:\.\d{1,2})?",
            RegexOptions.Compiled);

        /// <summary>ISO-8601 dates, e.g. <c>2025-01-15</c>.</summary>
        private static readonly Regex _isoDate = new(
            @"\b\d{4}-\d{2}-\d{2}\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Natural-language dates, e.g. <c>January 15</c>, <c>Jan 15, 2025</c>,
        /// <c>15 January 2025</c>.
        /// </summary>
        private static readonly Regex _naturalDate = new(
            @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|" +
            @"Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)" +
            @"\.?\s+\d{1,2}(?:,?\s+\d{4})?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Data-transfer speeds, e.g. <c>250 Mbps</c>, <c>1 Gbps</c>.</summary>
        private static readonly Regex _speed = new(
            @"\b\d+(?:\.\d+)?\s*(?:Mbps|Gbps|kbps)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Latency values, e.g. <c>12 ms</c>, <c>4ms</c>.</summary>
        private static readonly Regex _latency = new(
            @"\b\d+(?:\.\d+)?\s*ms\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Structured identifiers with an uppercase prefix, e.g.
        /// <c>CUST-78A21</c>, <c>TICK-001</c>, <c>ACC-10042</c>, <c>DISP-9934</c>.
        /// </summary>
        private static readonly Regex _structuredId = new(
            @"\b[A-Z]{2,10}-[\w-]+\b",
            RegexOptions.Compiled);

        /// <summary>Phone numbers in common US formats, e.g. <c>555-0100</c>, <c>555.0100</c>.</summary>
        private static readonly Regex _phone = new(
            @"\b\d{3}[-.\s]\d{3}[-.\s]\d{4}\b",
            RegexOptions.Compiled);

        /// <summary>Percentages, e.g. <c>12.5%</c>, <c>100%</c>.</summary>
        private static readonly Regex _percent = new(
            @"\b\d+(?:\.\d+)?\s*%",
            RegexOptions.Compiled);

        private static readonly IReadOnlyList<Regex> _allPatterns = new[]
        {
            _currency, _isoDate, _naturalDate, _speed, _latency,
            _structuredId, _phone, _percent
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that every verifiable claim in <paramref name="llmResponse"/> is
        /// traceable to at least one entry in <paramref name="toolOutputs"/>.
        /// </summary>
        /// <param name="llmResponse">
        /// The final text produced by the LLM for the current agent turn.
        /// </param>
        /// <param name="toolOutputs">
        /// Raw return values of every tool (kernel function) that was invoked during
        /// the same turn.  Pass an empty collection when no tools were called.
        /// </param>
        /// <returns>A <see cref="ReasoningValidation"/> with the verdict and supporting detail.</returns>
        public ReasoningValidation Validate(
            string llmResponse,
            IEnumerable<string> toolOutputs)
        {
            if (string.IsNullOrWhiteSpace(llmResponse))
                return ReasoningValidation.Unverifiable("LLM response was empty.");

            var outputs = (toolOutputs ?? Enumerable.Empty<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .ToList();

            if (outputs.Count == 0)
                return ReasoningValidation.Unverifiable(
                    "No tool outputs were provided; cannot verify claims.");

            var combinedOutputs = string.Join("\n", outputs);

            var claims = ExtractClaims(llmResponse);

            if (claims.Count == 0)
                return ReasoningValidation.Unverifiable(
                    "No verifiable facts (amounts, dates, IDs, speeds) found in the response.");

            var grounded   = new List<string>();
            var ungrounded = new List<string>();

            foreach (var claim in claims)
            {
                if (IsGrounded(claim, combinedOutputs))
                    grounded.Add(claim);
                else
                    ungrounded.Add(claim);
            }

            return ungrounded.Count > 0
                ? ReasoningValidation.Hallucinated(grounded, ungrounded)
                : ReasoningValidation.Grounded(grounded);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Extracts all distinct verifiable tokens from <paramref name="text"/> by
        /// applying every pattern in <see cref="_allPatterns"/>.
        /// </summary>
        private static IReadOnlyList<string> ExtractClaims(string text)
        {
            // Use a case-insensitive set to deduplicate overlapping matches
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in _allPatterns)
            {
                foreach (Match m in pattern.Matches(text))
                {
                    var value = NormalizeWhitespace(m.Value);
                    if (!string.IsNullOrEmpty(value))
                        seen.Add(value);
                }
            }

            return seen.ToList();
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="claim"/> appears verbatim
        /// (case-insensitive, whitespace-normalised) in <paramref name="combinedOutputs"/>.
        /// </summary>
        private static bool IsGrounded(string claim, string combinedOutputs) =>
            combinedOutputs.Contains(
                NormalizeWhitespace(claim),
                StringComparison.OrdinalIgnoreCase);

        private static string NormalizeWhitespace(string value) =>
            Regex.Replace(value, @"\s+", " ").Trim();
    }
}

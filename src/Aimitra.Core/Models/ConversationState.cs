using System;
using System.Collections.Generic;

#pragma warning disable CS1591  // self-documenting property names

namespace Aimitra.Core.Models
{
    /// <summary>
    /// Holds all mutable facts about an ongoing session.
    /// A single instance is created at the start of a conversation and
    /// passed to every agent hop; agents update it in place so each turn
    /// automatically has the context accumulated by prior turns.
    /// </summary>
    public class ConversationState
    {
        // ── Session ───────────────────────────────────────────────────────────
        public string SessionId    { get; set; } = Guid.NewGuid().ToString();
        public string PlanId       { get; set; } = string.Empty;
        public string CurrentTopic { get; set; } = string.Empty;
        public string Locale       { get; set; } = "en_US";

        /// <summary>Topic names that were active at least once this session.</summary>
        public Dictionary<string, bool> VisitedAgents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // ── Customer (populated by the verification agent) ────────────────────
        public bool   CustomerVerified { get; set; } = false;
        public string CustomerId       { get; set; } = string.Empty;
        public string CustomerName     { get; set; } = string.Empty;
        public string CustomerEmail    { get; set; } = string.Empty;
        public string PhoneNumber      { get; set; } = string.Empty;
        public string AccountNumber    { get; set; } = string.Empty;
        public string ServicePlan      { get; set; } = string.Empty;
        public string AddressOnFile    { get; set; } = string.Empty;

        // ── Billing ───────────────────────────────────────────────────────────
        public decimal BillingBalance       { get; set; }
        public string  BillingDueDate       { get; set; } = string.Empty;
        public string  BillingDisputeId     { get; set; } = string.Empty;
        public string  BillingDisputeStatus { get; set; } = string.Empty;

        // ── Technical support ─────────────────────────────────────────────────
        public string TicketId          { get; set; } = string.Empty;
        public string TicketStatus      { get; set; } = string.Empty;
        public string InternetSpeedTier { get; set; } = string.Empty;
        public string ModemStatus       { get; set; } = string.Empty;
        public string DiagnosticResults { get; set; } = string.Empty;

        // ── Plan management ───────────────────────────────────────────────────
        public string  PlanName        { get; set; } = string.Empty;
        public decimal PlanPrice       { get; set; }
        public bool    UpgradeEligible { get; set; } = false;
        public string  PromoCode       { get; set; } = string.Empty;

        // ── Conversation transcript (last N turns, oldest first) ──────────────
        private readonly List<ConversationTurn> _turns = new();

        /// <summary>Ordered record of every user/agent message this session.</summary>
        public IReadOnlyList<ConversationTurn> Turns => _turns;

        /// <summary>Appends a turn to the transcript.</summary>
        public void AddTurn(string role, string content)
            => _turns.Add(new ConversationTurn(role, content, DateTimeOffset.UtcNow));

        /// <summary>
        /// Returns a text block of the last <paramref name="maxTurns"/> turns,
        /// suitable for injecting into a system or user prompt.
        /// </summary>
        public string GetRecentTranscript(int maxTurns = 6)
        {
            var start = Math.Max(0, _turns.Count - maxTurns);
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < _turns.Count; i++)
            {
                var t = _turns[i];
                sb.AppendLine($"{t.Role}: {t.Content}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Serialises the verified-customer facts as a compact context header.
        /// Returns an empty string when no customer has been verified yet.
        /// </summary>
        public string GetCustomerContextHeader()
        {
            if (!CustomerVerified) return string.Empty;

            var parts = new List<string>
            {
                $"Customer: {CustomerName}",
                $"Account: {AccountNumber}",
                $"Plan: {ServicePlan}"
            };
            if (!string.IsNullOrEmpty(TicketId))   parts.Add($"Open ticket: {TicketId}");
            if (!string.IsNullOrEmpty(BillingDisputeId)) parts.Add($"Dispute: {BillingDisputeId}");

            return "[" + string.Join(" | ", parts) + "]";
        }
    }

    /// <summary>A single message in the conversation transcript.</summary>
    public record ConversationTurn(string Role, string Content, DateTimeOffset Timestamp);
}

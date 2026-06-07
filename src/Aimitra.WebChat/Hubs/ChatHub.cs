using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using METASYNAPSE.Services.Orchestration;
using METASYNAPSE.WebChat.Services;

namespace METASYNAPSE.WebChat.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ConnectionDiagnosticsService _diagnostics;
        private static readonly ConcurrentDictionary<string, DateTime> RecentMessageCache = new();
        private readonly TopicOrchestrator _orchestrator;
        private readonly IDocumentMemoryService _documentMemory;
        private readonly string _sharedCollection;
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(TopicOrchestrator orchestrator, IDocumentMemoryService documentMemory, ILogger<ChatHub> logger, ConnectionDiagnosticsService diagnostics)
        {
            _orchestrator = orchestrator;
            _documentMemory = documentMemory;
            _logger = logger;
            _diagnostics = diagnostics;
            _sharedCollection = Environment.GetEnvironmentVariable("KERNEL_MEMORY_SHARED_COLLECTION")?.Trim()
                ?? "METASYNAPSE";
        }

        public Task SetSessionCollection(string collection)
        {
            if (!string.IsNullOrWhiteSpace(collection))
            {
                _diagnostics.SetCollection(Context.ConnectionId, collection.Trim());
            }

            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _diagnostics.Unregister(Context.ConnectionId);
            _logger?.LogInformation("SignalR disconnected: {ConnectionId}", Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public override Task OnConnectedAsync()
        {
            _logger?.LogInformation("SignalR connected: {ConnectionId}", Context.ConnectionId);
            _diagnostics.Register(Context.ConnectionId, _sharedCollection);
            return base.OnConnectedAsync();
        }

        public async Task SendMessage(string user, string message)
        {
            // Deduplicate rapid duplicate messages from the same session+text
            try
            {
                var sessionCollection = _diagnostics.TryGetCollection(Context.ConnectionId, out var col) ? col : _sharedCollection;
                var dedupeKey = (sessionCollection ?? "") + "|" + (message ?? "");
                var now = DateTime.UtcNow;
                if (RecentMessageCache.TryGetValue(dedupeKey, out var last) && (now - last) < TimeSpan.FromSeconds(3))
                {
                    _logger?.LogInformation("Duplicate message suppressed for {ConnectionId}", Context.ConnectionId);
                    return;
                }
                RecentMessageCache[dedupeKey] = now;
                // prune old entries occasionally
                if (RecentMessageCache.Count > 1000)
                {
                    var cutoff = now - TimeSpan.FromMinutes(5);
                    foreach (var kv in RecentMessageCache.ToArray())
                    {
                        if (kv.Value < cutoff) RecentMessageCache.TryRemove(kv.Key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error while deduping message");
            }

            // Broadcast the user's message to everyone except the sender.
            var userMessageId = Guid.NewGuid().ToString("N");
            await Clients.Others.SendAsync("ReceiveMessage", user, message, userMessageId, false);

            // Route the message through the orchestrator to get bot response
            string botResponse;
            var assistantMessageId = Guid.NewGuid().ToString("N");
            try
            {
                await Clients.All.SendAsync("ReceiveMessage", "METASYNAPSE", "Thinking...", assistantMessageId, true);

                var contexts = new List<string>();
                _diagnostics.TryGetCollection(Context.ConnectionId, out var sessionCollection);

                var sharedMatches = await _documentMemory.AskAsync(message, _sharedCollection, topK: 3).ConfigureAwait(false);
                if (sharedMatches.Count > 0)
                {
                    var sharedContext = string.Join("\n\n", sharedMatches.Select(m => $"[{m.Source}] {m.Snippet}"));
                    contexts.Add($"SHARED CONTEXT ({_sharedCollection}):\n{sharedContext}");
                }

                if (!string.IsNullOrWhiteSpace(sessionCollection) &&
                    !string.Equals(sessionCollection, _sharedCollection, StringComparison.OrdinalIgnoreCase))
                {
                    var sessionMatches = await _documentMemory.AskAsync(message, sessionCollection, topK: 3).ConfigureAwait(false);
                    if (sessionMatches.Count > 0)
                    {
                        var sessionContext = string.Join("\n\n", sessionMatches.Select(m => $"[{m.Source}] {m.Snippet}"));
                        contexts.Add($"SESSION CONTEXT:\n{sessionContext}");
                    }
                }

                var enrichedMessage = contexts.Count == 0
                    ? message
                    : $"Use the following context to answer.\n\n{string.Join("\n\n", contexts)}\n\nUser question: {message}";

                botResponse = await _orchestrator.RunTurnAsync(
                        enrichedMessage,
                        cancellationToken: default,
                        intermediateResponseCallback: async partial =>
                        {
                            await Clients.All.SendAsync("ReceiveMessage", "METASYNAPSE", partial, assistantMessageId, true);
                        })
                    .ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                botResponse = $"(assistant error: {ex.Message})";
            }

            await Clients.All.SendAsync("ReceiveMessage", "METASYNAPSE", botResponse, assistantMessageId, false);
        }

        private async Task StreamAssistantResponseAsync(string response, string messageId)
        {
            var text = response ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                await Clients.All.SendAsync("ReceiveMessage", "METASYNAPSE", string.Empty, messageId, false);
                return;
            }

            var builder = new System.Text.StringBuilder();
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                builder.Append(token);
                builder.Append(' ');
                await Clients.All.SendAsync("ReceiveMessage", "METASYNAPSE", builder.ToString().TrimEnd(), messageId, true);
                await Task.Delay(18);
            }

            await Clients.All.SendAsync("ReceiveMessage", "METASYNAPSE", text, messageId, false);
        }
    }
}


using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using Aimitra.Services.Orchestration;
using Aimitra.WebChat.Services;

namespace Aimitra.WebChat.Hubs
{
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, string> ConnectionCollections = new(StringComparer.OrdinalIgnoreCase);
        private readonly TopicOrchestrator _orchestrator;
        private readonly IDocumentMemoryService _documentMemory;
        private readonly string _sharedCollection;

        public ChatHub(TopicOrchestrator orchestrator, IDocumentMemoryService documentMemory)
        {
            _orchestrator = orchestrator;
            _documentMemory = documentMemory;
            _sharedCollection = Environment.GetEnvironmentVariable("KERNEL_MEMORY_SHARED_COLLECTION")?.Trim()
                ?? "aimitra";
        }

        public Task SetSessionCollection(string collection)
        {
            if (!string.IsNullOrWhiteSpace(collection))
            {
                ConnectionCollections[Context.ConnectionId] = collection.Trim();
            }

            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            ConnectionCollections.TryRemove(Context.ConnectionId, out _);
            return base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string user, string message)
        {
            // Broadcast the user's message to everyone except the sender.
            var userMessageId = Guid.NewGuid().ToString("N");
            await Clients.Others.SendAsync("ReceiveMessage", user, message, userMessageId, false);

            // Route the message through the orchestrator to get bot response
            string botResponse;
            var assistantMessageId = Guid.NewGuid().ToString("N");
            try
            {
                await Clients.All.SendAsync("ReceiveMessage", "Aimitra", "Thinking...", assistantMessageId, true);

                var contexts = new List<string>();
                ConnectionCollections.TryGetValue(Context.ConnectionId, out var sessionCollection);

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
                            await Clients.All.SendAsync("ReceiveMessage", "Aimitra", partial, assistantMessageId, true);
                        })
                    .ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                botResponse = $"(assistant error: {ex.Message})";
            }

            await Clients.All.SendAsync("ReceiveMessage", "Aimitra", botResponse, assistantMessageId, false);
        }

        private async Task StreamAssistantResponseAsync(string response, string messageId)
        {
            var text = response ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                await Clients.All.SendAsync("ReceiveMessage", "Aimitra", string.Empty, messageId, false);
                return;
            }

            var builder = new System.Text.StringBuilder();
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                builder.Append(token);
                builder.Append(' ');
                await Clients.All.SendAsync("ReceiveMessage", "Aimitra", builder.ToString().TrimEnd(), messageId, true);
                await Task.Delay(18);
            }

            await Clients.All.SendAsync("ReceiveMessage", "Aimitra", text, messageId, false);
        }
    }
}

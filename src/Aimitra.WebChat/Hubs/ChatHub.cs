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
        private readonly string _factsCollection;

        public ChatHub(TopicOrchestrator orchestrator, IDocumentMemoryService documentMemory)
        {
            _orchestrator = orchestrator;
            _documentMemory = documentMemory;
            _factsCollection = Environment.GetEnvironmentVariable("KERNEL_MEMORY_FACTS_COLLECTION")?.Trim()
                ?? "facts";
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
            await Clients.Others.SendAsync("ReceiveMessage", user, message);

            // Route the message through the orchestrator to get bot response
            string botResponse;
            try
            {
                var contexts = new List<string>();
                ConnectionCollections.TryGetValue(Context.ConnectionId, out var sessionCollection);

                var factsMatches = await _documentMemory.AskAsync(message, _factsCollection, topK: 3).ConfigureAwait(false);
                if (factsMatches.Count > 0)
                {
                    var factsContext = string.Join("\n\n", factsMatches.Select(m => $"[{m.Source}] {m.Snippet}"));
                    contexts.Add($"FACTS CONTEXT:\n{factsContext}");
                }

                if (!string.IsNullOrWhiteSpace(sessionCollection))
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

                botResponse = await _orchestrator.RunTurnAsync(enrichedMessage).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                botResponse = $"(assistant error: {ex.Message})";
            }

            // Send assistant reply as coming from 'Aimitra'
            await Clients.All.SendAsync("ReceiveMessage", "Aimitra", botResponse);
        }
    }
}

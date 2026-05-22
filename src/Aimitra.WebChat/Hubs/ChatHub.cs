using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using Aimitra.Services.Orchestration;

namespace Aimitra.WebChat.Hubs
{
    public class ChatHub : Hub
    {
        private readonly TopicOrchestrator _orchestrator;

        public ChatHub(TopicOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        public async Task SendMessage(string user, string message)
        {
            // Broadcast the user's message to all clients
            await Clients.All.SendAsync("ReceiveMessage", user, message);

            // Route the message through the orchestrator to get bot response
            string botResponse;
            try
            {
                botResponse = await _orchestrator.RunTurnAsync(message).ConfigureAwait(false);
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

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aimitra.WebChat.Services
{
    public interface IPreviousSessionService
    {
        Task<List<string>> GetRecentSessionsAsync(int limit = 20);
        Task AddOrUpdateSessionAsync(string name);
        Task<string?> GetLastSessionNameAsync();
        Task<List<PreviousSessionMessage>> GetSessionMessagesAsync(string sessionName);
        Task AddMessageAsync(string sessionName, string? messageId, string user, string message, DateTime timestamp, bool isPartial = false);
    }
}

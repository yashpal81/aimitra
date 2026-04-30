using System.Threading;
using System.Threading.Tasks;

namespace Aimitra.Services.Interfaces
{
    public interface IOpenRouterClient
    {
        Task<string> GetChatCompletionAsync(string model, string prompt, CancellationToken cancellationToken = default);
    }
}

using System.Threading;
using System.Threading.Tasks;

namespace METASYNAPSE.Services.Interfaces
{
    public interface IOpenRouterClient
    {
        Task<string> GetChatCompletionAsync(string model, string prompt, CancellationToken cancellationToken = default);
    }
}


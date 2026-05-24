using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Aimitra.WebChat.Services
{
    public sealed class KnowledgeBasePlugin
    {
        private readonly IDocumentMemoryService _memory;

        public KnowledgeBasePlugin(IDocumentMemoryService memory)
        {
            _memory = memory;
        }

        [KernelFunction("query_knowledge_base")]
        [Description("Queries the active document collection and returns the most relevant snippets so the assistant can answer from retrieved context.")]
        public async Task<string> QueryKnowledgeBaseAsync(
            [Description("The user's question to ask against the knowledge base.")]
            string question,
            [Description("Optional collection name. If omitted, the last imported collection is used.")]
            string? collection = null,
            [Description("Maximum number of snippets to return.")]
            int topK = 5)
        {
            var matches = await _memory.AskAsync(question, collection, topK).ConfigureAwait(false);
            return JsonSerializer.Serialize(matches, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}

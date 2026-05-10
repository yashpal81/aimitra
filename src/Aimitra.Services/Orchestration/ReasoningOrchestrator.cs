using System;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;
using Aimitra.Services.Interfaces;

namespace Aimitra.Services.Orchestration
{
    public sealed class ReasoningOrchestrator
    {
        private readonly IOpenRouterClient _openRouterClient;
        private readonly string _model;

        public ReasoningOrchestrator(IOpenRouterClient openRouterClient, string model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free")
        {
            _openRouterClient = openRouterClient ?? throw new ArgumentNullException(nameof(openRouterClient));
            _model = string.IsNullOrWhiteSpace(model) ? throw new ArgumentException("Model cannot be empty.", nameof(model)) : model;
        }

        public async Task<ReasoningResult> GenerateSqlFromQuestionAsync(string question, DatabaseSchema schema, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException("Question is required.", nameof(question));
            }

            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            var prompt = DatabaseQueryTool.BuildPrompt(question, schema);
            var rawResponse = await _openRouterClient.GetChatCompletionAsync(_model, prompt, cancellationToken).ConfigureAwait(false);
            var sql = SanitizeSql(rawResponse);
            return new ReasoningResult(sql, prompt, rawResponse);
        }

        private static string SanitizeSql(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return string.Empty;
            }

            return rawResponse.Trim().Trim('`');
        }
    }
}

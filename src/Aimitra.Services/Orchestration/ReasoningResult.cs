using System;
using System.Collections.Generic;

namespace Aimitra.Services.Orchestration
{
    public sealed class ReasoningResult
    {
        public ReasoningResult(string sqlQuery, string prompt, string rawResponse, IReadOnlyCollection<string> trace = null)
        {
            SqlQuery = sqlQuery;
            Prompt = prompt;
            RawResponse = rawResponse;
            Trace = trace ?? Array.Empty<string>();
        }

        public string SqlQuery { get; }

        public string Prompt { get; }

        public string RawResponse { get; }

        public IReadOnlyCollection<string> Trace { get; }

        public bool HasResult => !string.IsNullOrWhiteSpace(SqlQuery);
    }
}

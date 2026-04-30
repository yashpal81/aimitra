namespace Aimitra.Services.Orchestration
{
    public sealed class ReasoningResult
    {
        public ReasoningResult(string sqlQuery, string prompt, string rawResponse)
        {
            SqlQuery = sqlQuery;
            Prompt = prompt;
            RawResponse = rawResponse;
        }

        public string SqlQuery { get; }

        public string Prompt { get; }

        public string RawResponse { get; }

        public bool HasResult => !string.IsNullOrWhiteSpace(SqlQuery);
    }
}

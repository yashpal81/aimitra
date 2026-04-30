using System.Text;
using Aimitra.Core.Models;

namespace Aimitra.Services.Orchestration
{
    public static class DatabaseQueryTool
    {
        public static string BuildPrompt(string userQuestion, DatabaseSchema schema)
        {
            var builder = new StringBuilder();
            builder.AppendLine("You are an intelligent SQL assistant.");
            builder.AppendLine("Use the schema context below to write a single SQL query that answers the user's question.");
            builder.AppendLine("If the question cannot be answered with the available schema, explain why.");
            builder.AppendLine();
            builder.AppendLine("Database schema:");
            builder.AppendLine(schema.ToString());
            builder.AppendLine();
            builder.AppendLine("User question:");
            builder.AppendLine(userQuestion);
            builder.AppendLine();
            builder.AppendLine("Respond with only the SQL query text. Do not include any markdown formatting.");

            return builder.ToString();
        }
    }
}

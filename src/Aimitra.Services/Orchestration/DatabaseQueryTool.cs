using System.Collections.Generic;
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

        public static string BuildSchemaSummary(DatabaseSchema schema)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Database: {schema.DatabaseName}");
            builder.AppendLine("Available tables:");

            foreach (var table in schema.Tables)
            {
                builder.AppendLine($"- {table.Schema}.{table.Name} ({table.Columns.Count} columns)");
            }

            return builder.ToString();
        }

        public static string BuildSchemaContext(DatabaseSchema schema)
        {
            return schema.ToString();
        }

        public static string BuildSemanticPrompt(string userQuestion, DatabaseSchema schema, IReadOnlyCollection<string> history)
        {
            var builder = new StringBuilder();
            builder.AppendLine("You are an intelligent SQL reasoning agent.");
            builder.AppendLine("You may call tools to inspect the database schema before producing a final SQL query and executing it.");
            builder.AppendLine();
            builder.AppendLine("Tools:");
            builder.AppendLine("- DB_SCHEMA: returns the database schema details.");
            builder.AppendLine("- WRITE_SQL: returns the results as SQL query only.");
            builder.AppendLine("- EXECUTE_SQL: returns the results of a SQL query only.");
            builder.AppendLine();
            builder.AppendLine("When you respond, return a JSON object with the following properties:");
            builder.AppendLine("{\n  \"thought\": string,\n  \"action\": \"DB_SCHEMA\" | \"WRITE_SQL\" | \"FINISH\",\n  \"action_input\": string\n}");
            builder.AppendLine();
            builder.AppendLine("If you need more schema detail, use action DB_SCHEMA.");
            builder.AppendLine("If you have a final SQL query, use action WRITE_SQL and put the SQL in action_input.");
            builder.AppendLine();
            builder.AppendLine("If you need to execute a SQL query, use action EXECUTE_SQL and put the SQL in action_input.");
            builder.AppendLine("If you have a final SQL query, use action EXECUTE_SQL and put the SQL in action_input.");
            builder.AppendLine();
         
            builder.AppendLine("Use only valid JSON. Do not include any markdown or extra text outside the JSON object.");
            builder.AppendLine();
            builder.AppendLine("Question:");
            builder.AppendLine(userQuestion);
            builder.AppendLine();
            builder.AppendLine("Known schema summary:");
            builder.AppendLine(BuildSchemaSummary(schema));

            if (history != null && history.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("History:");
                foreach (var item in history)
                {
                    builder.AppendLine(item);
                }
            }

            return builder.ToString();
        }
    }
}

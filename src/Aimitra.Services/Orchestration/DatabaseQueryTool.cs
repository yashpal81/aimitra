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
            // builder.AppendLine("You are an intelligent SQL reasoning agent.");
            // builder.AppendLine("Break down the problem into logical steps to solve it using the database schema.");
            // builder.AppendLine();
            // builder.AppendLine("CRITICAL RESPONSE FORMAT:");
            // builder.AppendLine("- Return ONLY a valid JSON array.");
            // builder.AppendLine("- NO markdown formatting, NO code fences, NO backticks, NO explanation text.");
            // builder.AppendLine("- Each step must be a JSON object with exactly these fields:");
            // builder.AppendLine("  * \"step\": an integer (1, 2, 3, etc.)");
            // builder.AppendLine("  * \"content\": a descriptive string");
            // builder.AppendLine();
            // builder.AppendLine("Example response (no other text):");
            // builder.AppendLine("[");
            // builder.AppendLine("  { \"step\": 1, \"content\": \"Identify the required tables from the schema.\" },");
            // builder.AppendLine("  { \"step\": 2, \"content\": \"Determine the join conditions between tables.\" },");
            // builder.AppendLine("  { \"step\": 3, \"content\": \"Formulate the SQL query based on requirements.\" },");
            // builder.AppendLine("  { \"step\": 4, \"content\": \"Execute the query and return results.\" }");
            // builder.AppendLine("]");
            // builder.AppendLine();
            // // builder.AppendLine("Database Schema Summary:");
            // // builder.AppendLine(BuildSchemaSummary(schema));
            // builder.AppendLine("resources:");
            // builder.AppendLine("Database name : SalesforceCoder");
            // builder.AppendLine("Database description : A PostgreSQL database for storing SalesforceCoder application data");
            // builder.AppendLine("Database provider : postgres");
            // builder.AppendLine();
            // builder.AppendLine("User Question:");
            // builder.AppendLine(userQuestion);
            // builder.AppendLine();
            // builder.AppendLine("<CRITICAL_INSTRUCTION>");
            // builder.AppendLine("1. Respond with ONLY the JSON array.");
            // builder.AppendLine("2. Do NOT include any text before or after the JSON.");
            // builder.AppendLine("3. Do NOT use markdown code blocks (```json ... ```).");
            // builder.AppendLine("4. Each object must have \"step\" (integer) and \"content\" (string) fields.");
            // builder.AppendLine("5. Number steps sequentially starting from 1.");
            // builder.AppendLine("</CRITICAL_INSTRUCTION>");
            builder.AppendLine("Do not write down plans, text steps, or mock schemas. If you need information about a database schema or need to run a query, you must execute the corresponding tool immediately. Wait for the tool's output before continuing your analysis.");
            builder.AppendLine();
            builder.AppendLine("User Question:");
            //builder.AppendLine("Generate a sample solution for problem where Id is 7 from the problems stored inside the SalesforceCoder database.");
            //builder.AppendLine("Give me the name of highest scorer in leaderboard inside the SalesforceCoder database.");
            builder.AppendLine("Friendly greeting for the Yashpal Sharma no need to check database or execute sql query just send a simple greeting. and also let me know if you have used the greeting plugin or not.");
            
            builder.AppendLine();
            if (history != null && history.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Previous context:");
                foreach (var item in history)
                {
                    builder.AppendLine(item);
                }
            }

            return builder.ToString();
        }
    }
}

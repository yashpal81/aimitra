using System;
using System.Net.Http;
using System.Threading.Tasks;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
using Aimitra.Services.Metadata;
using Aimitra.Services.OpenRouter;
using Aimitra.Services.Orchestration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI; // Essential for AddOpenAIChatCompletion
using Microsoft.SemanticKernel.ChatCompletion;
using System.Collections.Generic;
namespace Aimitra.ConsoleApp
{
    class Program
    {

  /* static async Task Main()
    {
         var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();
        var httpClient = new HttpClient();
        var kernel = builder.AddOpenAIChatCompletion("nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free", new Uri("https://openrouter.ai/api/v1"), apiKey, string.Empty, "openrouter", null).Build();
        Console.WriteLine(kernel != null);
 var chat = kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();
DatabaseSchema schema = BuildSampleSchema();
var history = new List<string>();
var prompt = DatabaseQueryTool.BuildSemanticPrompt("List order totals", schema, history);
                var chatHistory = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                chatHistory.AddUserMessage(prompt);
                var result =  await chat.GetChatMessageContentAsync(chatHistory, kernel: kernel, cancellationToken: default).ConfigureAwait(false);
                string rawResponse = result?.Content ?? string.Empty;
               Console.WriteLine(rawResponse); 


    }
*/

        static async Task Main(string[] args)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("OPENROUTER_API_KEY environment variable is required.");
                return;
            }

            var provider = Environment.GetEnvironmentVariable("DB_PROVIDER")?.Trim().ToLowerInvariant();
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            IDbMetadataService metadataService = provider switch
            {
                "sqlserver" => new SqlServerMetadataService(),
                "postgres" => new PostgresMetadataService(),
                _ => null
            };

            DatabaseSchema schema;
            if (metadataService != null && !string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine($"Loading schema from {provider}...");
                try
                {
                    schema = await metadataService.GetSchemaAsync(connectionString).ConfigureAwait(false);
                    Console.WriteLine($"Loaded schema for database '{schema.DatabaseName}' with {schema.Tables.Count} tables.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load schema: {ex.Message}");
                    return;
                }
            }
            else
            {
                Console.WriteLine("No DB_PROVIDER/DB_CONNECTION_STRING provided or provider unsupported. Using sample schema.");
                schema = BuildSampleSchema();
            }

            using (var httpClient = new HttpClient())
            {
               // var openRouterClient = new OpenRouterClient(httpClient, apiKey);
                var orchestrator = new SemanticKernelOrchestrator(apiKey);

                var question = "List each customer and their total order amount for orders placed in the last 30 days.";
                try
                {
                    var result = await orchestrator.GenerateSqlFromQuestionAsync(question, schema).ConfigureAwait(false);

                    Console.WriteLine("=== Generated SQL ===");
                    Console.WriteLine(result.SqlQuery);
                    Console.WriteLine();
                    Console.WriteLine("=== Raw response ===");
                    Console.WriteLine(result.RawResponse);
                    Console.WriteLine();
                    Console.WriteLine("=== Reasoning trace ===");
                    foreach (var step in result.Trace)
                    {
                        Console.WriteLine(step);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Reasoning failed:");
                    Console.WriteLine(ex.ToString());
                    return;
                }
            }
        }

        private static DatabaseSchema BuildSampleSchema()
        {
            return new DatabaseSchema(
                databaseName: "SampleDb",
                tables: new[]
                {
                    new TableDefinition(
                        schema: "dbo",
                        name: "Orders",
                        columns: new[]
                        {
                            new ColumnDefinition("OrderId", "int", false, string.Empty, 1, true, false),
                            new ColumnDefinition("CustomerId", "int", false, string.Empty, 2, false, true),
                            new ColumnDefinition("OrderDate", "datetime", false, string.Empty, 3, false, false),
                            new ColumnDefinition("TotalAmount", "decimal(18,2)", false, string.Empty, 4, false, false)
                        },
                        foreignKeys: new[]
                        {
                            new ForeignKeyDefinition("CustomerId", "dbo", "Customers", "CustomerId", "FK_Orders_Customers")
                        },
                        primaryKeyColumns: new[] { "OrderId" }),
                    new TableDefinition(
                        schema: "dbo",
                        name: "Customers",
                        columns: new[]
                        {
                            new ColumnDefinition("CustomerId", "int", false, string.Empty, 1, true, false),
                            new ColumnDefinition("Name", "nvarchar(100)", false, string.Empty, 2, false, false),
                            new ColumnDefinition("Email", "nvarchar(200)", true, string.Empty, 3, false, false)
                        },
                        foreignKeys: Array.Empty<ForeignKeyDefinition>(),
                        primaryKeyColumns: new[] { "CustomerId" })
                });
        }
    }
}

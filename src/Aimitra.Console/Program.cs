using System;
using System.Net.Http;
using System.Threading.Tasks;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
using Aimitra.Services.Metadata;
using Aimitra.Services.OpenRouter;
using Aimitra.Services.Orchestration;
using Aimitra.ConsoleApp.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI; // Essential for AddOpenAIChatCompletion
using Microsoft.SemanticKernel.ChatCompletion;
using System.Collections.Generic;
namespace Aimitra.ConsoleApp
{
    class Program
    {


        static async Task Main(string[] args)
        {
            var environmentName = Environment.GetEnvironmentVariable("AIMITRA_ENVIRONMENT")?.Trim();
            EnvFileLoader.Load(environmentName);

            var apiKey = Environment.GetEnvironmentVariable("API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.WriteLine("API_KEY environment variable is required.");
                return;
            }

            var openAIURL = Environment.GetEnvironmentVariable("OPENAI_URL");
            if (string.IsNullOrWhiteSpace(openAIURL))
            {
                Console.WriteLine("OPENAI_URL environment variable is required.");
                return;
            }            
            var openAIModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
            if (string.IsNullOrWhiteSpace(openAIModel))
            {
                Console.WriteLine("OPENAI_MODEL environment variable is required.");
                return;
            }

            var provider = Environment.GetEnvironmentVariable("DB_PROVIDER")?.Trim().ToLowerInvariant();
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");

            Console.WriteLine($"Using environment: {environmentName}");
            Console.WriteLine($"Using OpenAI URL: {openAIURL}");
            Console.WriteLine($"Using OpenAI Model: {openAIModel}");
            Console.WriteLine($"Using DB Provider: {provider}");
            Console.WriteLine($"Using DB Connection String: {connectionString}");
                        
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
                var orchestrator = new SemanticKernelOrchestrator(apiKey, openAIModel, openAIURL);

                //var question = "List each customer and their total order amount for orders placed in the last 30 days.";
                var question = "give solution of any problem from the problems stored in database table";
     
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

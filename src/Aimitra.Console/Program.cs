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
using Microsoft.SemanticKernel.Connectors.Google; // Using Google AI Studio



using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Google;
using System.Security.Cryptography;
using Aimitra.SemanticRouteService;



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

            var presidioEndpoint = Environment.GetEnvironmentVariable("PRESIDIO_ENDPOINT");
            Console.WriteLine($"Using environment: {environmentName}");
            Console.WriteLine($"Using OpenAI URL: {openAIURL}");
            Console.WriteLine($"Using OpenAI Model: {openAIModel}");
            Console.WriteLine($"Using DB Provider: {provider}");
            Console.WriteLine($"Using DB Connection String: {connectionString}");
            Console.WriteLine($"Using Presidio Endpoint: {presidioEndpoint}");


            // IDbMetadataService metadataService = provider switch
            // {
            //     "sqlserver" => new SqlServerMetadataService(),
            //     "postgres" => new PostgresMetadataService(),
            //     _ => null
            // };

            // DatabaseSchema schema;
            // if (metadataService != null && !string.IsNullOrWhiteSpace(connectionString))
            // {
            //     Console.WriteLine($"Loading schema from {provider}...");
            //     try
            //     {
            //         schema = await metadataService.GetSchemaAsync(connectionString).ConfigureAwait(false);
            //         Console.WriteLine($"Loaded schema for database '{schema.DatabaseName}' with {schema.Tables.Count} tables.");
            //     }
            //     catch (Exception ex)
            //     {
            //         Console.WriteLine($"Failed to load schema: {ex.Message}");
            //         return;
            //     }
            // }
            // else
            // {
            //     Console.WriteLine("No DB_PROVIDER/DB_CONNECTION_STRING provided or provider unsupported. Using sample schema.");
            //     schema = BuildSampleSchema();
            // }

            using (var httpClient = new HttpClient())
            {
                
                string userQuestion ="Predict Yashpal sharma future performance using astrology."; //"Give me the name of highest scorer in leaderboard inside the SalesforceCoder database?"; //"Greet Yashpal Sharma?";
                string routeAgent= getRouteForQuestion(userQuestion).Result;

              //  return;
            
                var orchestrator = new SemanticKernelOrchestrator(routeAgent,apiKey, openAIModel, openAIURL, presidioEndpoint);

                //var question = "List each customer and their total order amount for orders placed in the last 30 days.";
                var question = "give solution of any problem from the problems stored in database table";
     
                try
                {
                    DatabaseSchema schema = BuildSampleSchema();
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

        static void ExecuteTargetPipeline(string route, string query)
            {
                switch (route)
                {
                    case "Salesforce_Architect":
                        // Create custom Kernel context loading Salesforce-specific native functions/plugins
                        Console.WriteLine("🚀 Bootstrapping Salesforce execution workspace...");
                        break;
                    case "Momentum_Trading":
                        // Create isolated calculation context hosting technical analysis algorithms
                        Console.WriteLine("📈 Spinning up automated calculation systems...");
                        break;
                    default:
                        // Route directly to standard chat completions
                        Console.WriteLine("🌐 Forwarding to broad knowledge foundation models...");
                        break;
                }
            }

        private static async Task<string> getRouteForQuestion(string question)
        {
                    string geminiApiKey = Environment.GetEnvironmentVariable("API_KEY") ?? "YOUR_KEY";

// 1. Initialize our localized embedding engine
        var embeddingService = new GoogleAITextEmbeddingGenerationService(
            modelId: "gemini-embedding-001", 
            apiKey: geminiApiKey
        );

        // 2. Instantiate and configure the router with an intentional threshold
        var router = new SemanticRouter(embeddingService, scoreThreshold: 0.52f);

        Console.WriteLine("⚙️ Pre-calculating semantic route matrix...");
        
        await router.RegisterRouteAsync("Salesforce_Evaluation", new() {
            "Get Database schema for SalesforceCoder",
            "Write a SOQL query to fetch all Problems with difficulty Easy from the SalesforceCoder database",
            "Write a SOQL query to fetch the name and email of the user with highest score in the leaderboard from the SalesforceCoder database",
            "Write a SOQL query to fetch the name of the problem which has been attempted maximum number of times from the SalesforceCoder database"
        });

        await router.RegisterRouteAsync("GreetingPlugin", new() {
            "Greeting message to any user",
            "Not doing any database operation just send a simple greeting message to the user",
            "Send a friendly greeting to the user Yashpal Sharma",
            " Just send a simple greeting message to the user Yashpal Sharma without doing any database operation"
        });
        await router.RegisterRouteAsync("AstrologerPlugin", new() {
            "Provide astrological reading for any person",
            "Provide astrological reading for Yashpal Sharma",
            "Predict Yashpal Sharma future work and life using astrology."
            });

        // 3. Test incoming user requests
        string[] testQueries = {
            "Greet Yashpal Sharma?"//,
            // "What is the average temperature in Jaipur today?"
        };

        foreach (var query in testQueries)
        {
            Console.WriteLine($"\n📥 User Query: \"{query}\"");
            
            // Routing operation takes mere milliseconds, completely avoiding heavy LLM parsing
            string targetRoute = await router.RouteAsync(query);
            
            Console.WriteLine($"🔀 Route Selected: [{targetRoute}]");
            return targetRoute?.ToString();
            // Execute conditional code loops safely based on exact intent
//            ExecuteTargetPipeline(targetRoute, query);
        }

            // In production, this would vectorize the question and compare against route embeddings
            return "Fallback_Generic_Agent";
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Aimitra.ConsoleApp.Configuration;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
using Aimitra.SamplePlugins.Plugins;
using Aimitra.SemanticRouteService;
using Aimitra.Services.Metadata;
using Aimitra.Services.Orchestration;
using Aimitra.Services.Plugins;
#pragma warning disable SKEXP0001
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using Microsoft.SemanticKernel.Connectors.OpenAI;
#pragma warning restore SKEXP0001



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

            var presidioEndpoint = Environment.GetEnvironmentVariable("PRESIDIO_ENDPOINT") ?? string.Empty;
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

            // --- Build topics: each maps a domain description to its scoped plugin set ---
            var topics = new Topic[]
            {
                new Topic(
                    Name:        "DatabaseTools",
                    Description: "Answer questions about databases, generate SQL queries, retrieve schema " +
                                 "information, or solve problems stored in database tables.",
                    Actions:     new[] { KernelPluginFactory.CreateFromObject(new DatabasePlugin(), "DatabaseTools") }),

                new Topic(
                    Name:        "GreetingPlugin",
                    Description: "Send a friendly greeting or welcome message to a user by name. " +
                                 "Use this for any request that is purely a greeting with no database operation.",
                    Actions:     new[] { KernelPluginFactory.CreateFromObject(new SampleGreetingPlugin(), "GreetingTools") }),

                new Topic(
                    Name:        "AstrologerPlugin",
                    Description: "Provide astrological readings, horoscopes, or future predictions for a person " +
                                 "based on their name and date of birth.",
                    Actions:     new[] { KernelPluginFactory.CreateFromObject(new AstrologerPlugin(), "AstrologyTools") }),
            };

            // Create the kernel orchestrator (routing + LLM execution)
            var kernelOrchestrator = new SemanticKernelOrchestrator(
                routeAgent:       "topic_selector",
                apiKey:           apiKey,
                model:            openAIModel,
                endpoint:         openAIURL,
                presidioEndpoint: presidioEndpoint,
                topics:           topics);

            // Wrap in TopicOrchestrator — adds cross-turn ConversationState
            // Pass domain ITopicAgent implementations here when they exist (Phase 3).
            var orchestrator = new TopicOrchestrator(kernelOrchestrator);

            // --- Multi-turn conversation loop ---
            Console.WriteLine();
            Console.WriteLine($"Session: {orchestrator.State.SessionId}");
            Console.WriteLine("Aimitra ready. Type a message and press Enter. Leave blank to exit.");
            Console.WriteLine(new string('-', 60));

            while (true)
            {
                Console.Write("You: ");
                var userInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(userInput))
                    break;

                try
                {
                    var response = await orchestrator
                        .RunTurnAsync(userInput)
                        .ConfigureAwait(false);

                    // Show active topic and transition chain
                    var lastTransitions = orchestrator.TransitionLog
                        .TakeLast(topics.Length)
                        .Select(t => t.ToAgent ?? t.FromAgent)
                        .Distinct();
                    var pipelineLabel = orchestrator.State.CurrentTopic.Length > 0
                        ? orchestrator.State.CurrentTopic
                        : "(no topic)";

                    Console.WriteLine();
                    Console.WriteLine($"[Topic: {pipelineLabel} | Visited: {string.Join(", ", orchestrator.State.VisitedAgents.Keys)}]");
                    Console.WriteLine($"Agent: {response}");
                    Console.WriteLine(new string('-', 60));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
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
#pragma warning disable SKEXP0070
        var embeddingService = new GoogleAITextEmbeddingGenerationService(
            modelId: "gemini-embedding-001", 
            apiKey: geminiApiKey
        );
#pragma warning restore SKEXP0070

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
            return targetRoute?.ToString() ?? string.Empty;
            // Execute conditional code loops safely based on exact intent
//            ExecuteTargetPipeline(targetRoute, query);
        }

            // In production, this would vectorize the question and compare against route embeddings
            return "Fallback_Generic_Agent";
        }

    }
}

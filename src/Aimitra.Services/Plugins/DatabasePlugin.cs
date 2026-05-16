

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;
using System.ComponentModel;
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
namespace Aimitra.Services.Plugins
{
public class DatabasePlugin
{
    [KernelFunction, Description("Gets database schema information by database name")]
    public string GetDatabaseSchema(string databaseName)
    {
        var provider = Environment.GetEnvironmentVariable("DB_PROVIDER")?.Trim().ToLowerInvariant();
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            IDbMetadataService metadataService = provider switch
            {
                "sqlserver" => new SqlServerMetadataService(),
                "postgres" => new PostgresMetadataService(),
                _ => null
            };


         DatabaseSchema schema = new DatabaseSchema();
            if (metadataService != null && !string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine($"Loading schema from {provider}...");
                try
                {
                    schema = metadataService.GetSchemaAsync(connectionString).Result;
                    Console.WriteLine($"Loaded schema for database '{schema.DatabaseName}' with {schema.Tables.Count} tables.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load schema: {ex.Message}");
                    return $"Failed to load schema: {ex.Message}";
                }
            }
            else
            {
                Console.WriteLine("No DB_PROVIDER/DB_CONNECTION_STRING provided or provider unsupported. Using sample schema.");
                //schema = "";//BuildSampleSchema();
            }
            return $"Schema for {databaseName} is available. Database Schema:  {JsonSerializer.Serialize(schema) }    .";
    } 

    [KernelFunction, Description("Executes a SQL query")]
    public string ExecuteSql(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return string.Empty;
            }
            var provider = Environment.GetEnvironmentVariable("DB_PROVIDER")?.Trim().ToLowerInvariant();
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
            IDbMetadataService metadataService = provider switch
            {
                "sqlserver" => new SqlServerMetadataService(),
                "postgres" => new PostgresMetadataService(),
                _ => null
            };

        string result  = metadataService.ExecuteQueryAsJsonAsync(connectionString, sql).Result;
        Console.WriteLine($"Executed SQL: {sql}");
        Console.WriteLine($"Query result: {result}");
        return result;
        }
}

}

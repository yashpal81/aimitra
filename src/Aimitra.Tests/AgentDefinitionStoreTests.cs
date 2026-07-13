using System;
using System.IO;
using METASYNAPSE.WebChat.Models;
using METASYNAPSE.WebChat.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace MetaSynapse.Tests;

public class AgentDefinitionStoreTests
{
    [Fact]
    public void Save_PersistsDefinitionsInSqliteDatabase()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metasynapse-agent-store-tests", Guid.NewGuid().ToString("N"));
        var environment = new FakeWebHostEnvironment(tempRoot);
        var store = new AgentDefinitionStore(environment);

        var definition = new AgentDefinition
        {
            Name = "Finance Assistant",
            Description = "Handles finance questions",
            WelcomeMessage = "Hello from SQLite"
        };

        var filePath = store.Save(definition);

        var databasePath = Path.Combine(tempRoot, "App_Data", "agent-definitions", "definitions.sqlite");

        Assert.True(File.Exists(databasePath), "Expected the SQLite database file to be created.");
        Assert.False(string.IsNullOrWhiteSpace(filePath));

        var reloaded = store.Load(filePath);

        Assert.NotNull(reloaded);
        Assert.Equal(definition.Name, reloaded!.Definition.Name);
        Assert.Equal(definition.Description, reloaded.Definition.Description);
        Assert.Equal(definition.WelcomeMessage, reloaded.Definition.WelcomeMessage);
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public FakeWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            WebRootPath = Path.Combine(contentRootPath, "wwwroot");
            ApplicationName = "MetaSynapse.Tests";
            EnvironmentName = "Development";
        }

        public string ApplicationName { get; set; }
        public string WebRootPath { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; }
    }
}

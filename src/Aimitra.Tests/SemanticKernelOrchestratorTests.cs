using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;
using Aimitra.Services.Interfaces;
using Aimitra.Services.Orchestration;
using Moq;
using Xunit;

namespace Aimitra.Tests
{
    public class SemanticKernelOrchestratorTests
    {
        [Fact]
        public async Task GenerateSqlFromQuestionAsync_ReturnsSqlFromWriteSqlAction()
        {
            var mockOpenRouter = new Mock<IOpenRouterClient>();
            mockOpenRouter.SetupSequence(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("{\"thought\":\"Inspect schema\",\"action\":\"DB_SCHEMA\",\"action_input\":\"Need details\"}")
                .ReturnsAsync("{\"thought\":\"Produce SQL\",\"action\":\"WRITE_SQL\",\"action_input\":\"SELECT CustomerId, TotalAmount FROM dbo.Orders;\"}");

            var orchestrator = new SemanticKernelOrchestrator(mockOpenRouter.Object);
            var schema = new DatabaseSchema(
                databaseName: "TestDb",
                tables: new List<TableDefinition>
                {
                    new TableDefinition(
                        schema: "dbo",
                        name: "Orders",
                        columns: new List<ColumnDefinition>
                        {
                            new ColumnDefinition("CustomerId", "int", false, string.Empty, 1, true, false),
                            new ColumnDefinition("TotalAmount", "decimal(18,2)", false, string.Empty, 2, false, false)
                        },
                        foreignKeys: new List<ForeignKeyDefinition>(),
                        primaryKeyColumns: new List<string> { "CustomerId" })
                });

            var result = await orchestrator.GenerateSqlFromQuestionAsync("List order totals", schema);
             
            Assert.Equal("SELECT CustomerId, TotalAmount FROM dbo.Orders;", result.SqlQuery);
            Assert.Contains("Thought: Produce SQL", result.Trace);
            mockOpenRouter.Verify(c => c.GetChatCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }
    }
}

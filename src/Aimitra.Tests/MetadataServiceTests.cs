using System.Collections.Generic;
using System.Threading.Tasks;
using Aimitra.Core.Models;
using Aimitra.Services.Metadata;
using Xunit;

namespace Aimitra.Tests
{
    public class MetadataServiceTests
    {
        [Fact]
        public async Task GenerateContextStringAsync_ReturnsSchemaSummary()
        {
            var schema = new DatabaseSchema(
                databaseName: "TestDatabase",
                tables: new List<TableDefinition>
                {
                    new TableDefinition(
                        schema: "dbo",
                        name: "Customers",
                        columns: new List<ColumnDefinition>
                        {
                            new ColumnDefinition("CustomerId", "int", false, string.Empty, 1, true, false),
                            new ColumnDefinition("Name", "nvarchar(100)", true, string.Empty, 2, false, false)
                        },
                        foreignKeys: new List<ForeignKeyDefinition>(),
                        primaryKeyColumns: new List<string> { "CustomerId" }),
                });

            var service = new SqlServerMetadataService();
            var context = await service.GenerateContextStringAsync(schema);

            Assert.NotNull(context);
            Assert.Contains("Database: TestDatabase", context);
            Assert.Contains("Table: dbo.Customers", context);
            Assert.Contains("CustomerId int NOT NULL PK", context);
        }
    }
}

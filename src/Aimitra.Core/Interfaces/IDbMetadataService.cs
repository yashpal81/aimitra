using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;

namespace Aimitra.Core.Interfaces
{
    public interface IDbMetadataService
    {
        Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<TableDefinition>> GetTableDefinitionsAsync(string connectionString, CancellationToken cancellationToken = default);

        Task<string> GenerateContextStringAsync(DatabaseSchema schema);
    }
}

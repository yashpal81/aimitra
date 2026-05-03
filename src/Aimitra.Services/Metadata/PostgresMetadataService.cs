using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
using Npgsql;

namespace Aimitra.Services.Metadata
{
    public sealed class PostgresMetadataService : IDbMetadataService
    {
        /// <summary>
        /// Executes a SQL query and returns the result as a JSON array.
        /// </summary>
        public async Task<string> ExecuteQueryAsJsonAsync(string connectionString, string query, CancellationToken cancellationToken = default)
        {
            var results = new List<Dictionary<string, object?>>();
            using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            var row = new Dictionary<string, object?>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                row[reader.GetName(i)] = value;
                            }
                            results.Add(row);
                        }
                    }
                }
            }
            return System.Text.Json.JsonSerializer.Serialize(results);
        }
        public async Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            var tables = await GetTableDefinitionsAsync(connectionString, cancellationToken).ConfigureAwait(false);
            var databaseName = await GetDatabaseNameAsync(connectionString, cancellationToken).ConfigureAwait(false);
            return new DatabaseSchema(databaseName, tables);
        }

        public async Task<IReadOnlyCollection<TableDefinition>> GetTableDefinitionsAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            var columns = new List<ColumnRow>();
            var primaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var foreignKeys = new List<ForeignKeyRow>();

            using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
select table_schema, table_name, column_name, data_type, is_nullable, column_default, ordinal_position
from information_schema.columns
where table_schema not in ('pg_catalog', 'information_schema')
order by table_schema, table_name, ordinal_position";
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            columns.Add(new ColumnRow(
                                schema: reader.GetString(0),
                                table: reader.GetString(1),
                                columnName: reader.GetString(2),
                                dataType: reader.GetString(3),
                                isNullable: string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                                defaultValue: reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                                ordinalPosition: reader.GetInt32(6)));
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
select kcu.table_schema, kcu.table_name, kcu.column_name
from information_schema.table_constraints tc
join information_schema.key_column_usage kcu
  on tc.constraint_name = kcu.constraint_name
  and tc.table_schema = kcu.table_schema
  and tc.table_name = kcu.table_name
where tc.constraint_type = 'PRIMARY KEY'
order by kcu.table_schema, kcu.table_name, kcu.ordinal_position";
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            primaryKeys.Add(GetKey(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
select kcu.table_schema, kcu.table_name, kcu.column_name, ccu.table_schema as ref_schema, ccu.table_name as ref_table, ccu.column_name as ref_column, tc.constraint_name
from information_schema.table_constraints tc
join information_schema.key_column_usage kcu
  on tc.constraint_name = kcu.constraint_name
  and tc.table_schema = kcu.table_schema
  and tc.table_name = kcu.table_name
join information_schema.constraint_column_usage ccu
  on tc.constraint_name = ccu.constraint_name
  and tc.table_schema = ccu.constraint_schema
where tc.constraint_type = 'FOREIGN KEY'
order by kcu.table_schema, kcu.table_name, kcu.ordinal_position";
                    using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            foreignKeys.Add(new ForeignKeyRow(
                                sourceSchema: reader.GetString(0),
                                sourceTable: reader.GetString(1),
                                columnName: reader.GetString(2),
                                referencedTableSchema: reader.GetString(3),
                                referencedTableName: reader.GetString(4),
                                referencedColumnName: reader.GetString(5),
                                constraintName: reader.GetString(6)));
                        }
                    }
                }
            }

            var results = new List<TableDefinition>();
            var tableMap = new Dictionary<string, List<ColumnDefinition>>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                var key = GetTableKey(column.Schema, column.Table);
                if (!tableMap.TryGetValue(key, out var list))
                {
                    list = new List<ColumnDefinition>();
                    tableMap[key] = list;
                }

                list.Add(new ColumnDefinition(
                    name: column.ColumnName,
                    dataType: column.DataType,
                    isNullable: column.IsNullable,
                    defaultValue: column.DefaultValue,
                    ordinalPosition: column.OrdinalPosition,
                    isPrimaryKey: primaryKeys.Contains(GetKey(column.Schema, column.Table, column.ColumnName)),
                    isForeignKey: foreignKeys.Exists(fk => fk.SourceSchema == column.Schema && fk.SourceTable == column.Table && fk.ColumnName == column.ColumnName)));
            }

            foreach (var tableKey in tableMap.Keys)
            {
                var parts = tableKey.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var schema = parts[0];
                var table = parts[1];
                var tablePrimaryKeys = new List<string>();
                var tableForeignKeys = new List<ForeignKeyDefinition>();

                foreach (var pk in primaryKeys)
                {
                    var pkParts = pk.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pkParts.Length == 3 && string.Equals(pkParts[0], schema, StringComparison.OrdinalIgnoreCase) && string.Equals(pkParts[1], table, StringComparison.OrdinalIgnoreCase))
                    {
                        tablePrimaryKeys.Add(pkParts[2]);
                    }
                }

                foreach (var fk in foreignKeys)
                {
                    if (string.Equals(fk.SourceSchema, schema, StringComparison.OrdinalIgnoreCase) && string.Equals(fk.SourceTable, table, StringComparison.OrdinalIgnoreCase))
                    {
                        tableForeignKeys.Add(new ForeignKeyDefinition(
                            columnName: fk.ColumnName,
                            referencedTableSchema: fk.ReferencedTableSchema,
                            referencedTableName: fk.ReferencedTableName,
                            referencedColumnName: fk.ReferencedColumnName,
                            constraintName: fk.ConstraintName));
                    }
                }

                results.Add(new TableDefinition(
                    schema: schema,
                    name: table,
                    columns: tableMap[tableKey],
                    foreignKeys: tableForeignKeys,
                    primaryKeyColumns: tablePrimaryKeys));
            }

            return results;
        }

        public Task<string> GenerateContextStringAsync(DatabaseSchema schema)
        {
            return Task.FromResult(schema.ToString());
        }

        private async Task<string> GetDatabaseNameAsync(string connectionString, CancellationToken cancellationToken)
        {
            using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "select current_database();";
                    return (string)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static string GetKey(string schema, string table, string column)
        {
            return string.Concat(schema, "|", table, "|", column);
        }

        private static string GetTableKey(string schema, string table)
        {
            return string.Concat(schema, "|", table);
        }

        private sealed class ColumnRow
        {
            public ColumnRow(string schema, string table, string columnName, string dataType, bool isNullable, string defaultValue, int ordinalPosition)
            {
                Schema = schema;
                Table = table;
                ColumnName = columnName;
                DataType = dataType;
                IsNullable = isNullable;
                DefaultValue = defaultValue;
                OrdinalPosition = ordinalPosition;
            }

            public string Schema { get; }
            public string Table { get; }
            public string ColumnName { get; }
            public string DataType { get; }
            public bool IsNullable { get; }
            public string DefaultValue { get; }
            public int OrdinalPosition { get; }
        }

        private sealed class ForeignKeyRow
        {
            public ForeignKeyRow(string sourceSchema, string sourceTable, string columnName, string referencedTableSchema, string referencedTableName, string referencedColumnName, string constraintName)
            {
                SourceSchema = sourceSchema;
                SourceTable = sourceTable;
                ColumnName = columnName;
                ReferencedTableSchema = referencedTableSchema;
                ReferencedTableName = referencedTableName;
                ReferencedColumnName = referencedColumnName;
                ConstraintName = constraintName;
            }

            public string SourceSchema { get; }
            public string SourceTable { get; }
            public string ColumnName { get; }
            public string ReferencedTableSchema { get; }
            public string ReferencedTableName { get; }
            public string ReferencedColumnName { get; }
            public string ConstraintName { get; }
        }
    }
}

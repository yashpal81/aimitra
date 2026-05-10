using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Interfaces;
using Aimitra.Core.Models;
using Microsoft.Data.SqlClient;

namespace Aimitra.Services.Metadata
{
    public sealed class SqlServerMetadataService : IDbMetadataService
    {
        public async Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            var tables = await GetTableDefinitionsAsync(connectionString, cancellationToken).ConfigureAwait(false);
            var databaseName = await GetDatabaseNameAsync(connectionString, cancellationToken).ConfigureAwait(false);
            return new DatabaseSchema(databaseName, tables);
        }

        public async Task<string> ExecuteQueryAsJsonAsync(string connectionString, string query, CancellationToken cancellationToken = default)
        {
            var results = new List<Dictionary<string, object?>>();
            using (var connection = new SqlConnection(connectionString))
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
      

        public async Task<IReadOnlyCollection<TableDefinition>> GetTableDefinitionsAsync(string connectionString, CancellationToken cancellationToken = default)
        {
            var columns = new List<ColumnRow>();
            var primaryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var foreignKeys = new List<ForeignKeyRow>();

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
select TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, ORDINAL_POSITION, COLUMN_DEFAULT
from INFORMATION_SCHEMA.COLUMNS
order by TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION";
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
                                defaultValue: reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                                ordinalPosition: reader.GetInt32(8)));
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
select tc.TABLE_SCHEMA, tc.TABLE_NAME, kcu.COLUMN_NAME
from INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
join INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
  on tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
  and tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
  and tc.TABLE_NAME = kcu.TABLE_NAME
where tc.CONSTRAINT_TYPE = 'PRIMARY KEY'";
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
select fk.TABLE_SCHEMA, fk.TABLE_NAME, kcu.COLUMN_NAME, ccu.TABLE_SCHEMA as REF_SCHEMA, ccu.TABLE_NAME as REF_TABLE, ccu.COLUMN_NAME as REF_COLUMN, fk.CONSTRAINT_NAME
from INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
join INFORMATION_SCHEMA.TABLE_CONSTRAINTS fk on rc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
join INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu on kcu.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
join INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE ccu on ccu.CONSTRAINT_NAME = rc.UNIQUE_CONSTRAINT_NAME
where fk.CONSTRAINT_TYPE = 'FOREIGN KEY'";
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

            var tableMap = new Dictionary<string, List<ColumnDefinition>>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                var key = GetTableKey(column.Schema, column.Table);
                List<ColumnDefinition> list;
                if (!tableMap.TryGetValue(key, out list))
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
                    isForeignKey: foreignKeys.Exists(fk => string.Equals(fk.SourceSchema, column.Schema, StringComparison.OrdinalIgnoreCase) && string.Equals(fk.SourceTable, column.Table, StringComparison.OrdinalIgnoreCase) && string.Equals(fk.ColumnName, column.ColumnName, StringComparison.OrdinalIgnoreCase))));
            }

            var tables = new List<TableDefinition>();
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
                    var partsPk = pk.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                    if (partsPk.Length == 3 && string.Equals(partsPk[0], schema, StringComparison.OrdinalIgnoreCase) && string.Equals(partsPk[1], table, StringComparison.OrdinalIgnoreCase))
                    {
                        tablePrimaryKeys.Add(partsPk[2]);
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

                tables.Add(new TableDefinition(
                    schema: schema,
                    name: table,
                    columns: tableMap[tableKey],
                    foreignKeys: tableForeignKeys,
                    primaryKeyColumns: tablePrimaryKeys));
            }

            return tables;
        }

        public Task<string> GenerateContextStringAsync(DatabaseSchema schema)
        {
            return Task.FromResult(schema.ToString());
        }

        private async Task<string> GetDatabaseNameAsync(string connectionString, CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection.Database;
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

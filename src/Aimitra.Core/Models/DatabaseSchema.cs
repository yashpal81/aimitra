using System.Collections.Generic;
using System.Text;

namespace Aimitra.Core.Models
{
    public sealed class DatabaseSchema
    {
        public DatabaseSchema(){}
        public DatabaseSchema(string databaseName, IReadOnlyCollection<TableDefinition> tables)
        {
            DatabaseName = databaseName;
            Tables = tables;
        }

        public string DatabaseName { get; }

        public IReadOnlyCollection<TableDefinition> Tables { get; }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Database: {DatabaseName}");

            foreach (var table in Tables)
            {
                builder.AppendLine($"Table: {table.Schema}.{table.Name}");
                foreach (var column in table.Columns)
                {
                    var nullability = column.IsNullable ? "NULL" : "NOT NULL";
                    var primaryKey = column.IsPrimaryKey ? " PK" : string.Empty;
                    var foreignKey = column.IsForeignKey ? " FK" : string.Empty;
                    builder.AppendLine($"  - {column.Name} {column.DataType} {nullability}{primaryKey}{foreignKey}");
                }

                if (table.PrimaryKeyColumns.Count > 0)
                {
                    builder.AppendLine($"  Primary key: {string.Join(", ", table.PrimaryKeyColumns)}");
                }

                foreach (var foreignKey in table.ForeignKeys)
                {
                    builder.AppendLine($"  Foreign key: {foreignKey.ColumnName} -> {foreignKey.ReferencedTableSchema}.{foreignKey.ReferencedTableName}({foreignKey.ReferencedColumnName})");
                }
            }

            return builder.ToString();
        }
    }

    public sealed class TableDefinition
    {
        public TableDefinition(
            string schema,
            string name,
            IReadOnlyCollection<ColumnDefinition> columns,
            IReadOnlyCollection<ForeignKeyDefinition> foreignKeys,
            IReadOnlyCollection<string> primaryKeyColumns)
        {
            Schema = schema;
            Name = name;
            Columns = columns;
            ForeignKeys = foreignKeys;
            PrimaryKeyColumns = primaryKeyColumns;
        }

        public string Schema { get; }

        public string Name { get; }

        public IReadOnlyCollection<ColumnDefinition> Columns { get; }

        public IReadOnlyCollection<ForeignKeyDefinition> ForeignKeys { get; }

        public IReadOnlyCollection<string> PrimaryKeyColumns { get; }
    }

    public sealed class ColumnDefinition
    {
        public ColumnDefinition(
            string name,
            string dataType,
            bool isNullable,
            string defaultValue,
            int ordinalPosition,
            bool isPrimaryKey,
            bool isForeignKey)
        {
            Name = name;
            DataType = dataType;
            IsNullable = isNullable;
            DefaultValue = defaultValue;
            OrdinalPosition = ordinalPosition;
            IsPrimaryKey = isPrimaryKey;
            IsForeignKey = isForeignKey;
        }

        public string Name { get; }

        public string DataType { get; }

        public bool IsNullable { get; }

        public string DefaultValue { get; }

        public int OrdinalPosition { get; }

        public bool IsPrimaryKey { get; }

        public bool IsForeignKey { get; }
    }

    public sealed class ForeignKeyDefinition
    {
        public ForeignKeyDefinition(
            string columnName,
            string referencedTableSchema,
            string referencedTableName,
            string referencedColumnName,
            string constraintName)
        {
            ColumnName = columnName;
            ReferencedTableSchema = referencedTableSchema;
            ReferencedTableName = referencedTableName;
            ReferencedColumnName = referencedColumnName;
            ConstraintName = constraintName;
        }

        public string ColumnName { get; }

        public string ReferencedTableSchema { get; }

        public string ReferencedTableName { get; }

        public string ReferencedColumnName { get; }

        public string ConstraintName { get; }
    }
}

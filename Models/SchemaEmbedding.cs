namespace QueryAssist.Models;

/// <summary>
/// Represents a database schema element with its embedding vector for semantic search.
/// </summary>
public sealed class SchemaEmbedding
{
    public required string TableSchema { get; init; }
    public required string TableName { get; init; }
    public string? ColumnName { get; init; }
    public string? DataType { get; init; }
    public required string Description { get; init; }
    public required float[] Embedding { get; init; }
    public SchemaElementType ElementType { get; init; }
    public List<string>? RelatedTables { get; init; }
    public List<string>? ForeignKeyHints { get; init; }
}

public enum SchemaElementType
{
    Table,
    Column,
    Relationship
}
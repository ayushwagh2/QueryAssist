namespace QueryAssist.Models;

/// <summary>
/// Represents a schema element matched by semantic search with its similarity score.
/// </summary>
public sealed class SchemaSearchResult
{
    public required SchemaEmbedding Schema { get; init; }
    public required float SimilarityScore { get; init; }
}
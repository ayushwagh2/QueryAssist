using QueryAssist.Models;

namespace QueryAssist.Services;

/// <summary>
/// Service for managing schema embeddings and performing semantic schema search.
/// </summary>
public interface ISchemaEmbeddingService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SchemaSearchResult>> SearchRelevantSchemaAsync(string query, int maxResults, CancellationToken cancellationToken);
    Task RefreshEmbeddingsAsync(CancellationToken cancellationToken);
    bool IsInitialized { get; }
}
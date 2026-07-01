namespace QueryAssist.Services;

/// <summary>
/// Service for generating text embeddings using Gemini's embedding model.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken);
    Task<float[][]> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
    float CosineSimilarity(float[] a, float[] b);
}
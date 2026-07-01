namespace QueryAssist.Options;

public sealed class EmbeddingOptions
{
    public string Model { get; set; } = "text-embedding-004";
    public int EmbeddingDimension { get; set; } = 768;
    public int MaxSchemaResults { get; set; } = 10;
    public float MinSimilarityThreshold { get; set; } = 0.3f;
    public TimeSpan CacheRefreshInterval { get; set; } = TimeSpan.FromHours(1);
}
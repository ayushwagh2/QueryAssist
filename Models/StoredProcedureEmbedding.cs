using System;

namespace QueryAssist.Models;

public class StoredProcedureEmbedding
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

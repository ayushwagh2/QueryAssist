using System;
using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class FunctionEmbedding
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "dbo";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

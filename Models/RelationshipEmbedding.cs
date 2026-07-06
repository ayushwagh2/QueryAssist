using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class RelationshipEmbedding
{
    [JsonPropertyName("foreignKey")]
    public string ForeignKey { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}

using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class ExplainTableResponse
{
    [JsonPropertyName("tableName")]
    public string TableName { get; set; }

    [JsonPropertyName("similarityScore")]
    public float SimilarityScore { get; set; }

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; }

    public ExplainTableResponse(string tableName, float similarityScore, string explanation)
    {
        TableName = tableName;
        SimilarityScore = similarityScore;
        Explanation = explanation;
    }
}

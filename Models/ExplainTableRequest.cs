using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class ExplainTableRequest
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;
}

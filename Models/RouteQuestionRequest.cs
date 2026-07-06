using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class RouteQuestionRequest
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;
}

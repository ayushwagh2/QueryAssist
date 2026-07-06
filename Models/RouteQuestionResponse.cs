using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class RouteQuestionResponse
{
    [JsonPropertyName("collections")]
    public List<string> Collections { get; set; } = new List<string>();
}

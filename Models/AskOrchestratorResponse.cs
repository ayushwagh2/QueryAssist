using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class AskOrchestratorResponse
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("toolsExecuted")]
    public List<ToolExecutionData> ToolsExecuted { get; set; } = new List<ToolExecutionData>();

    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;
}

public class ToolExecutionData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public JsonObject? Arguments { get; set; }

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;
}

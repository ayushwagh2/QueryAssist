using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class BulkEmbedFunctionRequest
{
    [JsonPropertyName("functions")]
    public List<FunctionPayload> Functions { get; set; } = new();
}

public class FunctionPayload
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "dbo";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<FunctionParameterPayload> Parameters { get; set; } = new();

    [JsonPropertyName("definition")]
    public string Definition { get; set; } = string.Empty;
}

public class FunctionParameterPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("precision")]
    public int? Precision { get; set; }

    [JsonPropertyName("scale")]
    public int? Scale { get; set; }

    [JsonPropertyName("isOutput")]
    public bool IsOutput { get; set; }
}

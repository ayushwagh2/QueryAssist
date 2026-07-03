using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class BulkEmbedTableRequest
{
    [JsonPropertyName("tables")]
    public List<TablePayload> Tables { get; set; } = new();
}

public class TablePayload
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "dbo";

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("columns")]
    public List<ColumnPayload> Columns { get; set; } = new();
}

public class ColumnPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("nullable")]
    public string Nullable { get; set; } = "YES";
}

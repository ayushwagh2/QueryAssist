using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public class BulkEmbedRelationshipRequest
{
    [JsonPropertyName("relationships")]
    public List<RelationshipPayload> Relationships { get; set; } = new List<RelationshipPayload>();
}

public class RelationshipPayload
{
    [JsonPropertyName("foreignKey")]
    public string ForeignKey { get; set; }

    [JsonPropertyName("parentSchema")]
    public string ParentSchema { get; set; }

    [JsonPropertyName("parentTable")]
    public string ParentTable { get; set; }

    [JsonPropertyName("parentColumn")]
    public string ParentColumn { get; set; }

    [JsonPropertyName("referencedSchema")]
    public string ReferencedSchema { get; set; }

    [JsonPropertyName("referencedTable")]
    public string ReferencedTable { get; set; }

    [JsonPropertyName("referencedColumn")]
    public string ReferencedColumn { get; set; }

    [JsonPropertyName("onDelete")]
    public string OnDelete { get; set; }

    [JsonPropertyName("onUpdate")]
    public string OnUpdate { get; set; }
}

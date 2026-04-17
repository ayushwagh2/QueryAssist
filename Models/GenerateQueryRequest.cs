using System.Text.Json.Serialization;

namespace QueryAssist.Models;

public sealed class GenerateQueryRequest
{
    [JsonPropertyName("requirement")]
    public string? Requirement { get; init; }

    [JsonPropertyName("requirements")]
    public string? Requirements { get; init; }

    [JsonPropertyName("query")]
    public string? Query { get; init; }

    public string? GetRequirement()
    {
        if (!string.IsNullOrWhiteSpace(Requirement))
        {
            return Requirement;
        }

        if (!string.IsNullOrWhiteSpace(Requirements))
        {
            return Requirements;
        }

        if (!string.IsNullOrWhiteSpace(Query))
        {
            return Query;
        }

        return null;
    }
}

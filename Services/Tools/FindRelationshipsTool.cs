using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QueryAssist.Models;

namespace QueryAssist.Services.Tools;

public class FindRelationshipsTool : IAiTool
{
    public string Name => "FindRelationships";
    public string Description => "Search for table relationships and foreign keys based on a natural language question. Use this to find out how tables are connected to each other.";

    public JsonObject Parameters => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["question"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's question about relationships, like 'Which tables are related to Application?'"
            }
        },
        ["required"] = new JsonArray("question")
    };

    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<FindRelationshipsTool> _logger;

    public FindRelationshipsTool(
        IEmbeddingService embeddingService,
        ILogger<FindRelationshipsTool> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var question = arguments["question"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(question))
        {
            return "Error: Missing required argument 'question'.";
        }

        var filePath = "Relationships.json";
        if (!File.Exists(filePath))
        {
            return "Error: Relationships cache not found. Please queue relationships first.";
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var relationships = JsonSerializer.Deserialize<List<RelationshipEmbedding>>(json) ?? new List<RelationshipEmbedding>();

        if (relationships.Count == 0)
        {
            return "Error: Relationships cache is empty. Please queue relationships first.";
        }

        var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        // For relationships, we might want to return the top 3 instead of just 1, 
        // since a question like "Which tables are related to X" might have multiple answers.
        
        var matches = new List<(RelationshipEmbedding Rel, float Score)>();

        foreach (var rel in relationships)
        {
            if (rel.Embedding == null || rel.Embedding.Length != questionEmbedding.Length) continue;

            float similarity = _embeddingService.CosineSimilarity(questionEmbedding, rel.Embedding);
            matches.Add((rel, similarity));
        }

        matches.Sort((a, b) => b.Score.CompareTo(a.Score));

        // Take top 3
        var results = new List<string>();
        for (int i = 0; i < Math.Min(3, matches.Count); i++)
        {
            // Only include if it has at least some relevance (cosine similarity > 0.6 is a rough heuristic)
            if (matches[i].Score > 0.6f)
            {
                results.Add(matches[i].Rel.Text);
            }
        }

        if (results.Count == 0 && matches.Count > 0)
        {
            // If none crossed the threshold, just return the absolute best one
            results.Add(matches[0].Rel.Text);
        }

        if (results.Count == 0)
        {
            return "Could not find any matching relationships.";
        }

        _logger.LogInformation("Tool FindRelationships selected {Count} relationships.", results.Count);

        return string.Join("\n\n", results);
    }
}

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

public class ExplainTableTool : IAiTool
{
    public string Name => "ExplainTable";
    public string Description => "Search and explain a database table based on a natural language question. Use this when the user asks about a specific table's purpose or schema.";

    public JsonObject Parameters => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["question"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's question about the table."
            }
        },
        ["required"] = new JsonArray("question")
    };

    private readonly IEmbeddingService _embeddingService;
    private readonly IAiAgentService _aiAgentService;
    private readonly ILogger<ExplainTableTool> _logger;

    public ExplainTableTool(
        IEmbeddingService embeddingService,
        IAiAgentService aiAgentService,
        ILogger<ExplainTableTool> logger)
    {
        _embeddingService = embeddingService;
        _aiAgentService = aiAgentService;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var question = arguments["question"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(question))
        {
            return "Error: Missing required argument 'question'.";
        }

        var filePath = "Tables.json";
        if (!File.Exists(filePath))
        {
            return "Error: Tables cache not found. Please queue tables first.";
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var tables = JsonSerializer.Deserialize<List<TableEmbedding>>(json) ?? new List<TableEmbedding>();

        if (tables.Count == 0)
        {
            return "Error: Tables cache is empty. Please queue tables first.";
        }

        var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        TableEmbedding? bestTable = null;
        float bestSimilarity = -2.0f;

        foreach (var table in tables)
        {
            if (table.Embedding == null || table.Embedding.Length != questionEmbedding.Length) continue;

            float similarity = _embeddingService.CosineSimilarity(questionEmbedding, table.Embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestTable = table;
            }
        }

        if (bestTable == null || string.IsNullOrWhiteSpace(bestTable.Text))
        {
            return "Could not find a matching table for the given question.";
        }

        _logger.LogInformation("Tool ExplainTable selected {TableName} with similarity {Score:F3}", bestTable.Name, bestSimilarity);

        var explanation = await _aiAgentService.ExplainTableAsync(bestTable.Name, bestTable.Text, cancellationToken);
        
        return $"Table: {bestTable.Name}\nExplanation: {explanation}";
    }
}

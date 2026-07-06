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

public class ExplainSpTool : IAiTool
{
    public string Name => "ExplainStoredProcedure";
    public string Description => "Search and explain a Stored Procedure based on a natural language question. Use this when the user asks about business logic, data updates, or complex processing steps.";

    public JsonObject Parameters => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["question"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The user's question about the stored procedure."
            }
        },
        ["required"] = new JsonArray("question")
    };

    private readonly IEmbeddingService _embeddingService;
    private readonly IAiAgentService _aiAgentService;
    private readonly ILogger<ExplainSpTool> _logger;

    public ExplainSpTool(
        IEmbeddingService embeddingService,
        IAiAgentService aiAgentService,
        ILogger<ExplainSpTool> logger)
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

        var filePath = "StoredProcedures.json";
        if (!File.Exists(filePath))
        {
            return "Error: Stored procedures cache not found. Please queue SPs first.";
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var sps = JsonSerializer.Deserialize<List<StoredProcedureEmbedding>>(json) ?? new List<StoredProcedureEmbedding>();

        if (sps.Count == 0)
        {
            return "Error: Stored procedures cache is empty. Please queue SPs first.";
        }

        var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(question, cancellationToken);

        StoredProcedureEmbedding? bestSp = null;
        float bestSimilarity = -2.0f;

        foreach (var sp in sps)
        {
            if (sp.Embedding == null || sp.Embedding.Length != questionEmbedding.Length) continue;

            float similarity = _embeddingService.CosineSimilarity(questionEmbedding, sp.Embedding);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestSp = sp;
            }
        }

        if (bestSp == null || string.IsNullOrWhiteSpace(bestSp.Text))
        {
            return "Could not find a matching stored procedure for the given question.";
        }

        _logger.LogInformation("Tool ExplainStoredProcedure selected {SpName} with similarity {Score:F3}", bestSp.Name, bestSimilarity);

        var explanation = await _aiAgentService.ExplainStoredProcedureAsync(bestSp.Name, bestSp.Text, cancellationToken);
        
        return $"Stored Procedure: {bestSp.Name}\nExplanation: {explanation}";
    }
}
